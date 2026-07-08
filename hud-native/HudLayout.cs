// Coordenador de LAYOUT das telinhas do Jarvis (sessao + fan-out). Como cada janela e
// um PROCESSO separado, elas se organizam por arquivos num diretorio compartilhado (.slots):
// cada janela viva registra "<pid>.slot" = claim|altura|heartbeat|x|y|larg. Ao se posicionar,
// mede a soma das alturas das janelas ABERTAS ANTES dela (claim menor) e cai logo abaixo.
// Resultado: coluna alinhada a DIREITA, comecando abaixo dos botoes do app, descendo conforme
// abrem, e COMPACTANDO quando uma acima fecha (recalculo 1x/s). Janela arrastada sai do fluxo.
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class HudLayout {
  static int TOPGAP = 42;   // "logo abaixo dos botoes"; ajustavel AO VIVO via hud-dock.cfg (top=)
  static int MARGIN = 12;   // margem da borda direita; ajustavel AO VIVO via hud-dock.cfg (right=)
  const int GAP = 10;       // espaco vertical entre telinhas
  const long STALE = 4000;  // slot sem heartbeat ha >4s = janela morta -> ignora
  const long ORPHAN = 12000;// >12s -> apaga o arquivo orfao

  // posicao do dock configuravel: le <exeDir>\hud-dock.cfg (linhas "top=NN" / "right=NN"),
  // no maximo 1x/s -> editar o arquivo reposiciona as telinhas ao vivo. Sem arquivo = 42/12.
  static long cfgAt = 0;
  static void LoadCfg() {
    long now = Now();
    if (cfgAt != 0 && now - cfgAt < 1000) return;
    cfgAt = now;
    int top = 42, margin = 12;
    try {
      string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      string f = Path.Combine(exeDir, "hud-dock.cfg");
      if (File.Exists(f)) {
        foreach (string raw in File.ReadAllLines(f)) {
          string line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
          int eq = line.IndexOf('='); if (eq <= 0) continue;
          string k = line.Substring(0, eq).Trim().ToLowerInvariant();
          int n; if (!int.TryParse(line.Substring(eq + 1).Trim(), out n)) continue;
          if (k == "top" || k == "topgap") top = n;
          else if (k == "right" || k == "margin" || k == "rightmargin") margin = n;
        }
      }
    } catch {}
    TOPGAP = top; MARGIN = margin;
  }

  static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

  static string Dir() {
    string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    string d = Path.Combine(exeDir, ".slots");
    try { Directory.CreateDirectory(d); } catch {}
    return d;
  }

  // Renova o slot desta janela e devolve a posicao (canto direito, empilhada por ordem de
  // abertura). detached=true -> some do fluxo (janela arrastada) e so calcula sobre os outros.
  // mini=true -> capsula MINIMIZADA: estaciona no topo do canto direito, ACIMA de qualquer
  // telinha cheia, e empilha junto das outras minimizadas (uma embaixo da outra).
  public static System.Drawing.Point Place(int pid, long claim, int w, int h, bool detached, bool mini) {
    string dir = Dir();
    string me = Path.Combine(dir, pid + ".slot");
    long now = Now();
    LoadCfg();

    // soma alturas das janelas "acima": toda minimizada fica acima de qualquer cheia; dentro
    // do mesmo grupo, ordem de abertura (claim menor; empate -> pid menor).
    int aboveH = 0;
    try {
      foreach (var f in Directory.GetFiles(dir, "*.slot")) {
        string nm = Path.GetFileNameWithoutExtension(f);
        int opid; if (!int.TryParse(nm, out opid) || opid == pid) continue;
        string[] p;
        try { p = File.ReadAllText(f).Split('|'); } catch { continue; }
        long oclaim, ohb; int oh;
        if (p.Length < 3 || !long.TryParse(p[0], out oclaim) || !int.TryParse(p[1], out oh) || !long.TryParse(p[2], out ohb)) continue;
        if (now - ohb > ORPHAN) { try { File.Delete(f); } catch {} continue; }
        if (now - ohb > STALE) continue;                 // morta: nao ocupa espaco
        bool omini = p.Length >= 7 && p[6] == "1";
        bool above;
        if (omini != mini) above = omini;                // minimizada sempre acima de cheia
        else above = oclaim < claim || (oclaim == claim && opid < pid);
        if (above) aboveH += oh + GAP;
      }
    } catch {}

    var wa = Screen.PrimaryScreen.WorkingArea;
    int x = wa.Right - w - MARGIN;
    int y = wa.Top + TOPGAP + aboveH;
    if (y + h > wa.Bottom - 6) y = wa.Top + TOPGAP;      // estourou embaixo: volta ao topo
    if (x < wa.Left + 6) x = wa.Left + 6;

    if (detached) { try { if (File.Exists(me)) File.Delete(me); } catch {} }
    else { try { File.WriteAllText(me, claim + "|" + h + "|" + now + "|" + x + "|" + y + "|" + w + "|" + (mini ? "1" : "0")); } catch {} }
    return new System.Drawing.Point(x, y);
  }

  public static void Release(int pid) {
    try { string f = Path.Combine(Dir(), pid + ".slot"); if (File.Exists(f)) File.Delete(f); } catch {}
  }
}
