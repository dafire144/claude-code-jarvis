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
        int ow = FULLW; if (p.Length >= 6) { int tw; if (int.TryParse(p[5], out tw)) ow = tw; }   // largura (slot antigo/malformado -> assume cheia; TryParse zeraria ow)
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
        if (WriteSlot(me, claim + "|" + h + "|" + now + "|" + mine.X + "|" + mine.Y + "|" + w + "|" + (mini ? "1" : "0"))) { lastBody = body; lastWriteMs = now; }
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
    if (WriteSlot(Path.Combine(Dir(), pid + ".slot"), p[0] + "|" + p[1] + "|" + now + "|" + p[2] + "|" + p[3] + "|" + p[4] + "|" + p[5])) lastWriteMs = now;
  }

  // escrita ATOMICA do slot (tmp + rename): um leitor concorrente nunca ve conteudo parcial/vazio
  // (evita o "torn read" que fazia a janela sumir do arranjo de 1 tick e piscar sobreposicao).
  // Devolve SUCESSO -> o chamador so avanca o bookkeeping se gravou de verdade (falha != sucesso).
  // Em falha (ex: sharing violation) NAO cai pra WriteAllText (reintroduziria torn-read): retenta o
  // rename atomico; persistindo, prefere manter o slot antigo (valido) a escrever parcial.
  static bool WriteSlot(string path, string content) {
    for (int attempt = 0; attempt < 2; attempt++) {
      string tmp = path + (attempt == 0 ? ".tmp" : ".tmp2");
      try {
        File.WriteAllText(tmp, content);
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);   // rename atomico no NTFS
        return true;
      } catch { try { if (File.Exists(tmp)) File.Delete(tmp); } catch {} }
    }
    return false;
  }

  public static void Release(int pid) {
    try { string f = Path.Combine(Dir(), pid + ".slot"); if (File.Exists(f)) File.Delete(f); } catch {}
  }

  // Config "abrir minimizada" (compartilhada por TODAS as telinhas: sessao e fan-out): env
  // JARVIS_HUD_START_MINIMIZED (1/true/on/yes) OU o arquivo-flag <exeDir>\start-minimized.flag
  // (nao versionado). Env explicito (0/1) vence o flag. Ligada => a janela nasce mini no dock.
  public static bool WantStartMinimized() {
    try {
      string v = Environment.GetEnvironmentVariable("JARVIS_HUD_START_MINIMIZED");
      if (v != null) {
        v = v.Trim().ToLowerInvariant();
        if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
        if (v == "0" || v == "false" || v == "no" || v == "off") return false;
      }
    } catch {}
    try {
      string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      if (File.Exists(Path.Combine(exeDir, "start-minimized.flag"))) return true;
    } catch {}
    return false;
  }

  // ---- NASCER SUGADA NO BOTAO (v1.7.0, opt-in start-hidden.flag / JARVIS_HUD_START_HIDDEN):
  // o repouso padrao e TUDO dentro do botao. Janela nova nasce invisivel (o marcador
  // .hideall e armado no nascimento) e a pilula conta. Quando o usuario REVELA pelo botao,
  // grava-se .revealed: enquanto ele existir, janelas novas nascem visiveis (modo revelado);
  // re-esconder apaga o .revealed. Dock esvaziando, o botao limpa os dois -> a proxima
  // manha recomeca no padrao: tudo no botao. ----
  public static bool WantStartHidden() {
    try {
      string v = Environment.GetEnvironmentVariable("JARVIS_HUD_START_HIDDEN");
      if (v != null) {
        v = v.Trim().ToLowerInvariant();
        if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
        if (v == "0" || v == "false" || v == "no" || v == "off") return false;
      }
    } catch {}
    try {
      string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      if (File.Exists(Path.Combine(exeDir, "start-hidden.flag"))) return true;
    } catch {}
    return false;
  }
  static string RevealedPath() { return Path.Combine(Dir(), ".revealed"); }
  public static void SetRevealed() { try { File.WriteAllText(RevealedPath(), Now().ToString()); } catch {} }
  public static void ClearRevealed() { try { string f = RevealedPath(); if (File.Exists(f)) File.Delete(f); } catch {} }
  public static bool IsRevealed() { try { return File.Exists(RevealedPath()); } catch { return false; } }
  // chamada no NASCIMENTO da janela: arma o esconderijo se o padrao e "dentro do botao"
  public static bool BirthHidden() {
    if (!WantStartHidden() || IsRevealed()) return false;
    SetHidden();
    return true;
  }

  // ---- "ESCONDER TODAS" (v1.6.0): estado compartilhado alternado pelo BOTAO flutuante.
  // Marcador .hideall presente => TODAS as telinhas se escondem (Hide(); slot/heartbeat
  // continuam vivos, entao a posicao e o proprio botao ficam preservados); ausente =>
  // todas reaparecem onde estavam. Quem alterna e SO o botao (as janelas apenas obedecem).
  // Substitui o broadcast .minall da v1.5.1-1.5.3 (dobrar em capsula nao servia: as
  // capsulas do Davi ja nascem minimizadas -- o que ele quer e a TELA LIVRE, com volta).
  static string HiddenPath() { return Path.Combine(Dir(), ".hideall"); }
  public static void SetHidden() { try { File.WriteAllText(HiddenPath(), Now().ToString()); } catch {} }
  public static void ClearHidden() { try { string f = HiddenPath(); if (File.Exists(f)) File.Delete(f); } catch {} }
  public static bool IsHidden() { try { return File.Exists(HiddenPath()); } catch { return false; } }
  // quantas telinhas vivas (pro rotulo do botao no modo escondido)
  public static int LiveCount() {
    int n = 0; long now = Now();
    try {
      foreach (var f in Directory.GetFiles(Dir(), "*.slot")) {
        try { string[] p = File.ReadAllText(f).Split('|'); long hb; if (p.Length >= 3 && long.TryParse(p[2], out hb) && now - hb <= STALE) n++; } catch {}
      }
    } catch {}
    return n;
  }

  // ---- BOTAO FLUTUANTE "MINIMIZAR TODAS" (v1.5.2): janelinha dedicada que vive ao lado da
  // PRIMEIRA capsula do dock e so faz uma coisa: gravar o broadcast .minall. As telinhas
  // garantem a existencia dele (EnsureMinAllButton, chamada no DataTick de 1s): se o
  // heartbeat .btn-hb esta velho, relancam o processo (--minall-btn, instancia unica). ----
  public static string BtnHbPath() { return Path.Combine(Dir(), ".btn-hb"); }
  // posicao ATUAL do botao (o botao grava; as telinhas leem pra VOAR ate ele na succao)
  public static string BtnPosPath() { return Path.Combine(Dir(), ".btn-pos"); }
  public static void WriteBtnPos(int x, int y, int w, int h) { try { File.WriteAllText(BtnPosPath(), x + "|" + y + "|" + w + "|" + h); } catch {} }
  public static bool ReadBtnPos(out Rectangle r) {
    r = Rectangle.Empty;
    try {
      string[] p = File.ReadAllText(BtnPosPath()).Split('|');
      if (p.Length < 4) return false;
      r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
      return true;
    } catch { return false; }
  }
  public static void EnsureMinAllButton() {
    try {
      string hb = BtnHbPath();
      if (File.Exists(hb)) {
        long t; long.TryParse(File.ReadAllText(hb).Trim(), out t);
        if (Now() - t < 15000) return;                  // botao vivo (bate o hb a cada ~2s)
      }
      File.WriteAllText(hb, Now().ToString());          // carimba JA: N telinhas no mesmo segundo nao lancam N botoes (o mutex e a defesa final)
      string exe = Assembly.GetExecutingAssembly().Location;
      var psi = new System.Diagnostics.ProcessStartInfo(exe, "--minall-btn");
      psi.UseShellExecute = false; psi.CreateNoWindow = true;
      System.Diagnostics.Process.Start(psi);
    } catch {}
  }
  // PRIMEIRA capsula do dock (topo da coluna mais a direita): a ancora do botao flutuante.
  public static bool FirstSlot(out Rectangle r) {
    r = Rectangle.Empty; long now = Now(); int bestRight = int.MinValue;
    var cands = new List<Rectangle>();
    try {
      foreach (var f in Directory.GetFiles(Dir(), "*.slot")) {
        try {
          string[] p = File.ReadAllText(f).Split('|');
          if (p.Length < 6) continue;
          long hb; int x, y, w, h;
          if (!long.TryParse(p[2], out hb) || now - hb > STALE) continue;
          if (!int.TryParse(p[1], out h) || !int.TryParse(p[3], out x) || !int.TryParse(p[4], out y) || !int.TryParse(p[5], out w)) continue;
          cands.Add(new Rectangle(x, y, w, h));
          if (x + w > bestRight) bestRight = x + w;
        } catch {}
      }
    } catch {}
    bool has = false; Rectangle best = Rectangle.Empty;
    foreach (var c in cands) {
      if (c.X + c.Width < bestRight - 8) continue;      // so a coluna mais a direita
      if (!has || c.Y < best.Y) { best = c; has = true; }
    }
    r = best; return has;
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
    // ALTO N -> over-capacity PROFUNDO ate na tela grande: prova que a escada mantem os cantos
    // superior-direito distintos mesmo com muitas janelas (antes o teste so ia ate 8).
    { int[] a = new int[18]; for (int k = 0; k < 18; k++) a[k] = 1; scenarios.Add(a); }   // 18 cheias
    { int[] a = new int[24]; for (int k = 0; k < 24; k++) a[k] = 1; scenarios.Add(a); }   // 24 cheias
    { int[] a = new int[20]; for (int k = 0; k < 20; k++) a[k] = 2; scenarios.Add(a); }   // 20 fanouts
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

// ---- BOTAO FLUTUANTE "ESCONDER/MOSTRAR TODAS" (v1.6.0; nasceu como minimizar-todas na
// v1.5.2) ---- Janelinha sempre-no-topo, SEPARADA das telinhas, ao lado da primeira
// capsula do dock (a direita dela; sem folga na tela, encosta a esquerda). E um
// INTERRUPTOR estilo "mostrar area de trabalho": clique 1 esconde TODAS as telinhas
// (marcador .hideall; disco vira PILULA com a contagem), clique 2 traz todas de volta.
// Aparece quando ha telinha viva, some quando o dock esvazia (e encerra apos 1min,
// limpando o marcador; as telinhas relancam pelo heartbeat). Mutex = instancia unica.
public class MinAllButton : Form {
  const int BW = 24, BH = 24, PW = 54, GAP = 4;   // disco (mostrar) e pilula (escondido)
  static Color Ink1 = ColorTranslator.FromHtml("#121F17"), Ink2 = ColorTranslator.FromHtml("#070E09");
  static Color Amber = ColorTranslator.FromHtml("#E8B24A"), AmberMut = ColorTranslator.FromHtml("#BE9E6C"), BorderC = ColorTranslator.FromHtml("#C9A877");
  System.Windows.Forms.Timer tick; long lastHb = 0, emptySince = 0; double flash = 0; bool hover = false;
  Point lastPosWrote = new Point(-9999, -9999); int lastWWrote = 0;   // throttle do .btn-pos (so grava em mudanca)
  System.Threading.Mutex mx; bool got = false;

  protected override bool ShowWithoutActivation { get { return true; } }             // nao rouba o foco
  protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }   // toolwindow: fora do Alt-Tab

  public MinAllButton() {
    // mutex POR INSTALACAO (nao global): duas instalacoes na mesma maquina (ex.: repo de
    // dev + ~/.claude/jarvis) tem docks separados -> cada uma merece o seu botao
    string instKey = "0";
    try { instKey = Math.Abs(AppDomain.CurrentDomain.BaseDirectory.ToLowerInvariant().GetHashCode()).ToString(); } catch { /* ok */ }
    mx = new System.Threading.Mutex(true, "JarvisMinAllBtn_" + instKey, out got);
    if (!got) Environment.Exit(0);                                                   // ja existe um botao DESTA instalacao
    FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
    StartPosition = FormStartPosition.Manual; Size = new Size(BW, BH);
    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    var gp = new System.Drawing.Drawing2D.GraphicsPath(); gp.AddEllipse(0, 0, BW - 1, BH - 1); Region = new Region(gp);
    Cursor = Cursors.Hand; Opacity = 0.97;
    tick = new System.Windows.Forms.Timer(); tick.Interval = 250;
    tick.Tick += delegate { Poll(); }; tick.Start();
    Poll();
  }

  static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

  int CurBW() { return HudLayout.IsHidden() ? PW : BW; }
  void ApplyShape() {
    int w = CurBW();
    if (Width == w) return;
    Size = new Size(w, BH);
    var gp = new System.Drawing.Drawing2D.GraphicsPath();
    if (w > BH) gp.AddPath(RoundPill(w, BH), false); else gp.AddEllipse(0, 0, w - 1, BH - 1);
    Region = new Region(gp);
  }
  static System.Drawing.Drawing2D.GraphicsPath RoundPill(int w, int h) {
    var gp = new System.Drawing.Drawing2D.GraphicsPath(); float r = (h - 1) / 2f;
    gp.AddArc(0, 0, 2 * r, 2 * r, 90, 180); gp.AddArc(w - 1 - 2 * r, 0, 2 * r, 2 * r, 270, 180); gp.CloseFigure();
    return gp;
  }

  void Poll() {
    long now = NowMs();
    if (now - lastHb > 2000) { try { File.WriteAllText(HudLayout.BtnHbPath(), now.ToString()); } catch {} lastHb = now; }
    Rectangle first;
    if (HudLayout.FirstSlot(out first)) {
      emptySince = 0;
      ApplyShape();
      var wa = Screen.PrimaryScreen.WorkingArea;
      int w = CurBW();
      Point p;
      if (HudLayout.IsHidden()) {
        // pilula ASSUME O POSTO da primeira capsula (canto do dock) — as telinhas foram
        // sugadas pra ca; nada de pular pro lado (era o "teleporte" que o Davi viu)
        p = new Point(first.X + first.Width - w, first.Y);
      } else {
        int x = first.X + first.Width + GAP;                     // a direita da primeira capsula
        if (x + w > wa.Right - 2) x = first.X - GAP - w;         // sem folga -> a esquerda dela
        p = new Point(x, first.Y + 15);                          // alinhado a linha dos botoes da capsula
      }
      if (!Visible) { Location = p; Show(); }
      else if (Location != p) Location = p;
      if (Location != lastPosWrote || w != lastWWrote) { HudLayout.WriteBtnPos(Location.X, Location.Y, w, BH); lastPosWrote = Location; lastWWrote = w; }   // alvo da succao
      if (flash > 0) { flash -= 0.25; if (flash < 0) flash = 0; }
      Invalidate();                                              // pulso suave a 4fps (janelinha minuscula = barato)
    } else {
      if (Visible) Hide();
      if (emptySince == 0) emptySince = now;
      else if (now - emptySince > 60000) { HudLayout.ClearHidden(); HudLayout.ClearRevealed(); Close(); }   // dock vazio ha 1min: encerra e RESETA o modo (a proxima manha recomeca no padrao)
    }
  }

  protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
  protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
  protected override void OnMouseDown(MouseEventArgs e) {
    if (e.Button == MouseButtons.Left) {
      // interruptor + memoria do modo: revelado (janela nova nasce visivel) vs padrao
      // "dentro do botao" (com start-hidden.flag, janela nova nasce sugada)
      if (HudLayout.IsHidden()) { HudLayout.ClearHidden(); HudLayout.SetRevealed(); }
      else { HudLayout.SetHidden(); HudLayout.ClearRevealed(); }
      flash = 1; ApplyShape(); Invalidate();
    }
    base.OnMouseDown(e);
  }

  protected override void OnPaint(PaintEventArgs e) {
    try { Render(e.Graphics, Width, HudLayout.IsHidden(), HudLayout.LiveCount(), hover, flash, NowMs()); } catch {}
  }
  // desenho num metodo estatico p/ o modo QA (--btn-shot) reusar sem abrir janela
  public static void Render(Graphics g, int w, bool hidden, int count, bool hov, double fl, long clockMs) {
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    var rect = new Rectangle(0, 0, w, BH);
    double pulse = 0.5 + 0.5 * Math.Sin(clockMs / 500.0 * 1.1);
    int ba = (int)(150 + 60 * pulse); if (hov || fl > 0) ba = 255;
    using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Ink1, Ink2, 60f))
    using (var path = w > BH ? RoundPill(w, BH) : null) {
      if (path != null) g.FillPath(bg, path); else g.FillEllipse(bg, 0, 0, w - 1, BH - 1);
      using (var pen = new Pen(Color.FromArgb(ba, hov || fl > 0 ? Amber : BorderC), hov ? 1.8f : 1.3f)) {
        if (path != null) g.DrawPath(pen, path); else g.DrawEllipse(pen, 0.9f, 0.9f, w - 2.8f, BH - 2.8f);
      }
    }
    if (fl > 0) using (var fb = new SolidBrush(Color.FromArgb((int)(70 * fl), Amber))) g.FillRectangle(fb, 2, 2, w - 5, BH - 5);
    using (var hp = new Pen(hov ? Amber : AmberMut, 1.7f)) {
      hp.StartCap = System.Drawing.Drawing2D.LineCap.Round; hp.EndCap = System.Drawing.Drawing2D.LineCap.Round;
      if (!hidden) {
        // modo MOSTRANDO: chevrons pra baixo = "esconder todas" (tela livre)
        float cx = w / 2f, by = 6.5f;
        g.DrawLine(hp, cx - 4.5f, by, cx, by + 4); g.DrawLine(hp, cx, by + 4, cx + 4.5f, by);
        g.DrawLine(hp, cx - 4.5f, by + 6, cx, by + 10); g.DrawLine(hp, cx, by + 10, cx + 4.5f, by + 6);
      } else {
        // modo ESCONDIDO (pilula): pip do reator pulsando + contagem + chevron pra cima = "mostrar"
        float cy = BH / 2f;
        using (var gl = new SolidBrush(Color.FromArgb((int)(48 + 40 * pulse), Amber))) g.FillEllipse(gl, 5, cy - 7, 14, 14);
        using (var b = new SolidBrush(Amber)) g.FillEllipse(b, 9.6f, cy - 2.4f, 4.8f, 4.8f);
        using (var f = new Font("Consolas", 9f, FontStyle.Bold))
        using (var tb = new SolidBrush(Amber)) g.DrawString(count.ToString(), f, tb, 21, cy - 8);
        float hx = w - 15;
        g.DrawLine(hp, hx - 4, cy + 2, hx, cy - 2.5f); g.DrawLine(hp, hx, cy - 2.5f, hx + 4, cy + 2);
      }
    }
  }
  public static void Shot(string outPath, bool hidden) {
    int w = hidden ? PW : BW;
    using (var bmp = new Bitmap(w, BH)) {
      using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); Render(g, w, hidden, 3, false, 0, 250); }
      bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
    }
  }

  protected override void OnFormClosed(FormClosedEventArgs e) {
    try { if (got && mx != null) mx.ReleaseMutex(); } catch {}
    base.OnFormClosed(e);
  }
}
