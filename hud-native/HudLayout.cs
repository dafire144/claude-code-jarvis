// Coordenador de LAYOUT das telinhas do Jarvis (sessao + fan-out). Como cada janela e um
// PROCESSO separado, elas se organizam por arquivos num diretorio compartilhado (.slots):
// cada janela viva registra "<pid>.slot" = claim|altura|heartbeat|x|y|larg|mini.
//
// MODELO SANFONA (ordem de abertura): TODA janela viva calcula o MESMO arranjo global a
// partir do conjunto de slots -> todas concordam (fim de vaos/sobreposicoes por desacordo).
// As telinhas empilham na ordem em que abriram (claim), cada uma na SUA altura real; expandir
// so cresce a janela no lugar (empurra as de baixo), minimizar encolhe (puxa as de baixo).
// Quando a coluna nao cabe mais na tela, TRANSBORDA para uma nova coluna a ESQUERDA -> nunca
// empilha por cima nem some da tela ("em toda situacao da pra ver o que esta acontecendo").
// A matematica de empacotamento fica numa funcao PURA (Pack) -> testavel sem janela (SelfTest).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

static class HudLayout {
  static int TOPGAP = 42;      // "logo abaixo dos botoes"; ajustavel AO VIVO via hud-dock.cfg (top=)
  static int MARGIN = 12;      // margem da borda direita; ajustavel AO VIVO via hud-dock.cfg (right=)
  const int GAP = 10;          // espaco vertical entre telinhas
  const int FULLW = 380;       // largura de referencia (janela cheia) -> passo de coluna
  const int COLGAP = 12;       // espaco horizontal entre colunas quando transborda
  const int BOTTOMPAD = 6;     // folga da borda inferior
  const long STALE = 4000;     // slot sem heartbeat ha >4s = janela morta -> ignora
  const long ORPHAN = 12000;   // >12s -> apaga o arquivo orfao
  static string lastBody = null;   // ultima POSICAO gravada (sem o hb) -> throttle de escrita
  static long lastWriteMs = 0;

  // janela para o empacotador puro
  public struct Win { public long claim; public int h; public int pid; public int w; }

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

  // EMPACOTADOR PURO (sem I/O, sem tela): dado o conjunto de janelas + a area util, devolve a
  // posicao de cada pid. Ordem de abertura (claim; empate -> pid). Empilha do topo pra baixo,
  // encostando na direita; quando a proxima nao cabe antes do fim da tela, comeca uma coluna
  // NOVA a esquerda (passo = largura cheia). Determinista -> toda janela chega ao mesmo mapa.
  public static Dictionary<int, Point> Pack(List<Win> wins, Rectangle wa, int topgap, int margin) {
    var outp = new Dictionary<int, Point>();
    var items = new List<Win>(wins);
    items.Sort(delegate(Win a, Win b) {
      if (a.claim != b.claim) return a.claim < b.claim ? -1 : 1;
      return a.pid.CompareTo(b.pid);
    });
    // guarda o dock DENTRO da tela mesmo com hud-dock.cfg absurdo (top/right negativo ou gigante)
    if (topgap < 0) topgap = 0; else if (topgap > wa.Height - 120) topgap = Math.Max(0, wa.Height - 120);
    if (margin < 0) margin = 0; else if (margin > wa.Width - 120) margin = Math.Max(0, wa.Width - 120);
    int rightEdge = wa.Right - margin;
    int top = wa.Top + topgap;
    int bottom = wa.Bottom - BOTTOMPAD;
    int colPitch = FULLW + COLGAP;
    int maxCol = (rightEdge - (wa.Left + 6) - FULLW) / colPitch; if (maxCol < 0) maxCol = 0;   // colunas reais que cabem
    int col = 0, y = top, cascade = 0;
    for (int i = 0; i < items.Count; i++) {
      Win s = items[i];
      if (col <= maxCol && y != top && y + s.h > bottom) { col++; y = top; }   // coluna cheia -> proxima a esquerda
      if (col <= maxCol) {
        int sx = rightEdge - col * colPitch - s.w;                             // coluna real: encosta na direita, sempre na tela
        outp[s.pid] = new Point(sx, y);
        y += s.h + GAP;
      } else {
        // OVER-CAPACITY (mais janelas do que a tela comporta): impossivel sem sobrepor, mas
        // NAO empilha identico -> escada diagonal do topo-esquerdo. Cada canto superior-direito
        // (fechar/minimizar) fica num ponto DISTINTO e agarravel (nunca 100% coberto).
        int cx2 = wa.Left + 6 + (cascade % 5) * 26;
        int cy2 = top + (cascade % 5) * 30 + (cascade / 5) * 6;
        if (cy2 + s.h > bottom) cy2 = top + (cascade / 5) * 6;
        outp[s.pid] = new Point(cx2, cy2);
        cascade++;
      }
    }
    return outp;
  }

  // Le os slots vivos do disco (inclui o meu, com meus valores atuais de w/h) e devolve a lista
  // para o empacotador. Limpa orfaos e ignora janelas mortas (sem heartbeat).
  static List<Win> LiveWins(string dir, int pid, long claim, int w, int h, long now) {
    var list = new List<Win>();
    Win me; me.claim = claim; me.h = h; me.pid = pid; me.w = w; list.Add(me);
    try {
      foreach (var f in Directory.GetFiles(dir, "*.slot")) {
        string nm = Path.GetFileNameWithoutExtension(f);
        int opid; if (!int.TryParse(nm, out opid) || opid == pid) continue;
        string[] p;
        try { p = File.ReadAllText(f).Split('|'); } catch { continue; }
        if (p.Length < 3) continue;
        long oclaim, ohb; int oh;
        if (!long.TryParse(p[0], out oclaim) || !int.TryParse(p[1], out oh) || !long.TryParse(p[2], out ohb)) continue;
        if (now - ohb > ORPHAN) { try { File.Delete(f); } catch {} continue; }
        if (now - ohb > STALE) continue;                       // morta: nao ocupa espaco
        int ow = FULLW; if (p.Length >= 6) int.TryParse(p[5], out ow);   // largura (slot antigo -> assume cheia)
        Win o; o.claim = oclaim; o.h = oh; o.pid = opid; o.w = ow; list.Add(o);
      }
    } catch {}
    return list;
  }

  // Renova o slot desta janela e devolve a posicao (empacotamento global determinista).
  // detached=true -> some do fluxo (janela arrastada): calcula sobre os outros mas nao ocupa slot.
  // mini so entra no slot como metadado (o arranjo usa a altura h real, nao o flag).
  public static Point Place(int pid, long claim, int w, int h, bool detached, bool mini) {
    string dir = Dir();
    string me = Path.Combine(dir, pid + ".slot");
    long now = Now();
    LoadCfg();

    var wa = Screen.PrimaryScreen.WorkingArea;
    List<Win> wins = LiveWins(dir, pid, claim, w, h, now);
    if (detached) wins.RemoveAll(delegate(Win x) { return x.pid == pid; });   // arrastada nao participa
    var map = Pack(wins, wa, TOPGAP, MARGIN);

    Point mine;
    if (!map.TryGetValue(pid, out mine)) mine = new Point(wa.Right - MARGIN - w, wa.Top + TOPGAP);

    if (detached) { try { if (File.Exists(me)) File.Delete(me); } catch {} lastBody = null; }
    else {
      // so REESCREVE quando a posicao muda OU o heartbeat envelhece (>STALE/2) -> o rename atomico
      // nao roda a cada tick e o disco descansa (o poll rapido fica quase todo em leitura barata).
      string body = claim + "|" + h + "|" + mine.X + "|" + mine.Y + "|" + w + "|" + (mini ? "1" : "0");
      if (body != lastBody || now - lastWriteMs >= STALE / 2) {
        WriteSlot(me, claim + "|" + h + "|" + now + "|" + mine.X + "|" + mine.Y + "|" + w + "|" + (mini ? "1" : "0"));
        lastBody = body; lastWriteMs = now;
      }
    }
    return mine;
  }

  // renova SO o heartbeat do slot (mantendo a posicao) mesmo quando a janela NAO recalcula layout
  // (arrastando/morph/boot). Sem isso, segurar o mouse parado >STALE fazia a vizinha empacotar por
  // cima de uma janela viva (achado #6). Throttle proprio -> nao vira churn de escrita.
  public static void Touch(int pid) {
    if (lastBody == null) return;
    long now = Now();
    if (now - lastWriteMs < STALE / 2) return;
    string[] p = lastBody.Split('|');   // claim|h|x|y|w|mini
    if (p.Length < 6) return;
    WriteSlot(Path.Combine(Dir(), pid + ".slot"), p[0] + "|" + p[1] + "|" + now + "|" + p[2] + "|" + p[3] + "|" + p[4] + "|" + p[5]);
    lastWriteMs = now;
  }

  // escrita ATOMICA do slot (tmp + rename): um leitor concorrente nunca ve conteudo parcial/vazio
  // (evita o "torn read" que fazia a janela sumir do arranjo de 1 tick e piscar sobreposicao).
  static void WriteSlot(string path, string content) {
    try {
      string tmp = path + ".tmp";
      File.WriteAllText(tmp, content);
      if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);   // rename atomico no NTFS
    } catch {
      try { File.WriteAllText(path, content); } catch {}   // fallback: melhor um write nao-atomico do que nenhum
    }
  }

  public static void Release(int pid) {
    try { string f = Path.Combine(Dir(), pid + ".slot"); if (File.Exists(f)) File.Delete(f); } catch {}
  }

  // ---- AUTO-TESTE do invariante "zero sobreposicao / nada fora da tela" (jarvis-hud-wf.exe --layout-test) ----
  // Enumera cenarios (minis 182x54, cheias 380x300, casa-de-festas 342x190) em varias combinacoes
  // e telas, empacota com Pack e checa: nenhum par de janelas se sobrepoe e nenhuma sai da area util.
  static bool RectsOverlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) {
    return ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;
  }
  // quantas colunas o empacotamento precisa (mesma logica vertical do Pack)
  static int ColsNeeded(List<Win> wins, Rectangle wa, int topgap) {
    var items = new List<Win>(wins);
    items.Sort(delegate(Win a, Win b) { if (a.claim != b.claim) return a.claim < b.claim ? -1 : 1; return a.pid.CompareTo(b.pid); });
    int topY = wa.Top + topgap, bottom = wa.Bottom - BOTTOMPAD;
    int col = 0, y = topY;
    for (int i = 0; i < items.Count; i++) { Win s = items[i]; if (y != topY && y + s.h > bottom) { col++; y = topY; } y += s.h + GAP; }
    return col + 1;
  }
  public static int SelfTest(StringBuilder report) {
    // INVARIANTE (fisicamente alcancavel): dentro da CAPACIDADE da tela, ZERO sobreposicao.
    // Acima da capacidade (mais janelas cheias do que cabem) e impossivel sem sobrepor/esconder:
    // ai a regra e "degrada sem crash e com o canto agarravel na tela". O teste checa as duas.
    int fails = 0, cases = 0, fitCases = 0, overCap = 0;
    var screens = new Rectangle[] {
      new Rectangle(0, 0, 1920, 1032),   // 1080p (a tela do Davi)
      new Rectangle(0, 0, 1366, 728),    // notebook
      new Rectangle(0, 0, 2560, 1392),   // 1440p
      new Rectangle(0, 0, 1280, 600),    // baixinha (forca transbordo cedo)
    };
    int[][] sizes = new int[][] { new int[] { 182, 54 }, new int[] { 380, 300 }, new int[] { 342, 190 } };   // 0=mini 1=cheia 2=fanout
    var scenarios = new List<int[]>();
    scenarios.Add(new int[] { 0 });
    scenarios.Add(new int[] { 0, 0, 0 });
    scenarios.Add(new int[] { 0, 1, 0 });                       // mini, cheia, mini (o caso do Davi)
    scenarios.Add(new int[] { 1, 1 });
    scenarios.Add(new int[] { 0, 0, 0, 0, 2 });                 // 4 minis + casa de festas (o print)
    scenarios.Add(new int[] { 1, 0, 1, 0, 2, 0 });
    scenarios.Add(new int[] { 1, 1, 1, 1, 1, 1, 1, 1 });        // muitas cheias -> forca colunas (over-cap em tela pequena)
    scenarios.Add(new int[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }); // 20 minis
    scenarios.Add(new int[] { 2, 2, 2, 2, 2, 2, 2, 2 });        // varias casas de festa
    int pitch = FULLW + COLGAP;
    for (int si = 0; si < screens.Length; si++) {
      Rectangle wa = screens[si];
      int top = wa.Top + 42;
      int maxCols = Math.Max(1, (wa.Right - 12 - FULLW - wa.Left - 6) / pitch + 1);   // colunas cheias que cabem
      for (int sc = 0; sc < scenarios.Count; sc++) {
        cases++;
        var wins = new List<Win>();
        int[] plan = scenarios[sc];
        for (int i = 0; i < plan.Length; i++) {
          Win wv; wv.claim = 1000 + i; wv.pid = 100 + i; wv.w = sizes[plan[i]][0]; wv.h = sizes[plan[i]][1];
          wins.Add(wv);
        }
        var map = Pack(wins, wa, 42, 12);
        // ancora SEMPRE na tela (o canto superior-direito, com fechar/minimizar, tem que dar pra pegar)
        for (int i = 0; i < wins.Count; i++) {
          Point pt = map[wins[i].pid];
          if (pt.X < wa.Left || pt.X >= wa.Right || pt.Y < top || pt.Y >= wa.Bottom) {
            report.AppendLine("FALHA ancora-fora tela=" + si + " cenario=" + sc + " pid=" + wins[i].pid + " em (" + pt.X + "," + pt.Y + ")");
            fails++;
          }
        }
        int need = ColsNeeded(wins, wa, 42);
        if (need <= maxCols) {                                  // cabe -> ZERO sobreposicao e obrigatorio
          fitCases++;
          for (int i = 0; i < wins.Count; i++) {
            for (int j = i + 1; j < wins.Count; j++) {
              Point a = map[wins[i].pid], b = map[wins[j].pid];
              if (RectsOverlap(a.X, a.Y, wins[i].w, wins[i].h, b.X, b.Y, wins[j].w, wins[j].h)) {
                report.AppendLine("FALHA sobreposicao tela=" + si + " cenario=" + sc + " pids " + wins[i].pid + "+" + wins[j].pid);
                fails++;
              }
            }
          }
        } else {
          overCap++;
          // over-capacity: nao da pra evitar sobreposicao, mas o CANTO superior-direito (fechar/
          // minimizar) de CADA janela tem que ser distinto -> nenhuma fica 100% coberta/inatingivel.
          var corners = new HashSet<Point>();
          for (int i = 0; i < wins.Count; i++) {
            Point pt = map[wins[i].pid];
            if (!corners.Add(new Point(pt.X + wins[i].w, pt.Y))) {
              report.AppendLine("FALHA canto-coberto tela=" + si + " cenario=" + sc + " (2 janelas com o mesmo canto superior-direito)");
              fails++;
            }
          }
          report.AppendLine("INFO over-capacity (esperado, escada diagonal) tela=" + si + "(" + wa.Width + "x" + wa.Height + ") cenario=" + sc + " precisa " + need + " colunas, cabem " + maxCols);
        }
      }
    }
    report.AppendLine((fails == 0 ? "OK" : "FALHOU") + ": " + cases + " cenarios (" + fitCases + " dentro da capacidade, " + overCap + " over-capacity), " + fails + " violacoes.");
    return fails;
  }
}
