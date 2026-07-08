// J.A.R.V.I.S. — telinha nativa de FAN-OUT (substitui o CMD "Protocolo de Missao").
// Uso: jarvis-hud-wf.exe --fanout <missionFile.json>   (lancada pelo hud-launch.mjs)
//      jarvis-hud-wf.exe --fanout-shot <out.png>        (QA visual, frame sintetico)
// Le o arquivo de missao (hud\<id>.json escrito pelo hud-launch/hud-close):
//   { status, start, proto, agent, task, model, autoCloseSec, doneAt, cost_usd, tokens }
// Mostra o "enxame" trabalhando: nucleo orquestrador + agentes orbitando + conexoes
// pulsando, protocolo, agente, missao, cronometro e (ao concluir) tempo/tokens/custo R$.
// PERFORMANCE: anima ~15fps SO enquanto rodando e SO repinta a area do enxame
// (Invalidate(swarmRect)); dados relidos 1x/s. So transform/opacity no loop -> CPU baixo.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

class FanoutHud : Form {
  string file, dir;
  string proto = "", agent = "", task = "", model = "", statusRaw = "running";
  long start = 0, doneAt = 0, tokens = 0;
  double costUsd = 0; int autoCloseSec = 0;
  bool done = false;
  long bornMs = NowMs(), closeAt = 0;
  double phase = 0;
  bool dragging; Point dragStart;
  int pid; bool userMoved, movedDuringDrag;   // arrasto manual tira a janela do auto-layout
  WinTimer dataTimer, animTimer;
  Mutex mutex;

  static Color Ink1 = C("#121F17"), Ink2 = C("#070E09");
  static Color Amber = C("#E8B24A"), AmberBright = C("#F4C25C"), AmberMut = C("#BE9E6C"), AmberDeep = C("#8A6A2E");
  static Color TextC = C("#DCCDAB"), Online = C("#86E3A6"), BorderC = C("#C9A877"), Faint = C("#6C786E"), Red = C("#E8794C");
  static Color C(string h) { return ColorTranslator.FromHtml(h); }

  Font fHdr = new Font("Consolas", 10.5f, FontStyle.Bold);
  Font fTitle = new Font("Consolas", 12.5f, FontStyle.Bold);
  Font fStat = new Font("Consolas", 7.5f, FontStyle.Bold);
  Font fLbl = new Font("Consolas", 7f, FontStyle.Regular);
  Font fVal = new Font("Consolas", 9.5f, FontStyle.Bold);
  Font fBody = new Font("Segoe UI", 8.75f, FontStyle.Regular);
  Font fBig = new Font("Consolas", 16f, FontStyle.Bold);
  Font fTiny = new Font("Consolas", 6.9f, FontStyle.Regular);
  Font fClose = new Font("Consolas", 11f, FontStyle.Bold);

  const int W = 342, H = 190, PAD = 14, RAD = 15;
  static Rectangle closeRect = new Rectangle(W - 24, 8, 18, 18);
  static Rectangle swarmRect = new Rectangle(6, 50, 112, 134);
  const int SCX = 62, SCY = 120;   // centro do enxame

  public static void Run(string missionFile) {
    string id = Path.GetFileNameWithoutExtension(missionFile);
    bool made; var m = new Mutex(true, "JarvisFanout_" + id, out made);
    if (!made) return;                       // ja existe janela p/ essa missao
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new FanoutHud(missionFile, m));
  }

  public static void Shot(string outPng) { Shot(outPng, false); }
  public static void Shot(string outPng, bool asDone) {
    var f = new FanoutHud(null, null);
    f.Seed(); if (asDone) f.SeedDone();
    var bmp = new Bitmap(W, H);
    using (var g = Graphics.FromImage(bmp)) f.Render(g);
    bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
  }

  protected override CreateParams CreateParams {
    get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } // WS_EX_TOOLWINDOW: fora do Alt+Tab
  }

  FanoutHud(string missionFile, Mutex m) {
    file = missionFile; mutex = m;
    dir = file != null ? Path.GetDirectoryName(file) : "";

    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false; TopMost = true;
    ClientSize = new Size(W, H);
    StartPosition = FormStartPosition.Manual;
    BackColor = Ink2; DoubleBuffered = true;
    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    using (var gp = RoundedPath(0, 0, W, H, RAD)) Region = new Region(gp);

    if (m == null) return;   // modo --fanout-shot: sem timers/posicao

    pid = System.Diagnostics.Process.GetCurrentProcess().Id;
    Location = HudLayout.Place(pid, bornMs, W, H, false, false);
    ReadMission();

    dataTimer = new WinTimer(); dataTimer.Interval = 1000;
    dataTimer.Tick += delegate { DataTick(); };
    dataTimer.Start();

    animTimer = new WinTimer(); animTimer.Interval = 66;
    animTimer.Tick += delegate { phase += 0.14; Invalidate(swarmRect); };
    if (!done) animTimer.Start();
  }

  static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

  // parse simples (regex) do JSON de missao escrito pelo hud-launch/hud-close
  void ReadMission() {
    try {
      if (file == null || !File.Exists(file)) return;
      string s = File.ReadAllText(file);
      proto = Grp(s, "\"proto\":\"([^\"]*)\"");
      agent = Grp(s, "\"agent\":\"([^\"]*)\"");
      task = Unescape(Grp(s, "\"task\":\"([^\"]*)\""));
      model = Grp(s, "\"model\":\"([^\"]*)\"");
      statusRaw = Grp(s, "\"status\":\"([^\"]*)\"");
      start = Lng(s, "\"start\":(\\d+)");
      doneAt = Lng(s, "\"doneAt\":(\\d+)");
      tokens = Lng(s, "\"tokens\":(\\d+)");
      autoCloseSec = (int)Lng(s, "\"autoCloseSec\":(\\d+)");
      var mc = Regex.Match(s, "\"cost_usd\":([\\d.]+)");
      if (mc.Success) double.TryParse(mc.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out costUsd);
      done = statusRaw == "done" || doneAt > 0;
    } catch {}
  }
  static string Grp(string s, string pat) { var m = Regex.Match(s, pat); return m.Success ? m.Groups[1].Value : ""; }
  static long Lng(string s, string pat) { var m = Regex.Match(s, pat); long v = 0; if (m.Success) long.TryParse(m.Groups[1].Value, out v); return v; }
  static string Unescape(string s) { return s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", " "); }

  void DataTick() {
    ReadMission();
    long now = NowMs();
    // define quando fechar
    if (done) {
      if (closeAt == 0) { closeAt = (doneAt > 0 ? doneAt : now) + 8000; if (animTimer.Enabled) animTimer.Stop(); Invalidate(); }
    } else if (autoCloseSec > 0 && start > 0 && now - start > (long)autoCloseSec * 1000) {
      // processo em 2o plano sem sinal de fim: encerra o painel informativo
      if (closeAt == 0) closeAt = now + 200;
    }
    // trava de seguranca: nunca vive mais de 20 min
    if (now - bornMs > 20 * 60 * 1000) { Close(); return; }
    // arquivo de missao sumiu (faxina) -> encerra
    if (file != null && !File.Exists(file) && now - bornMs > 5000) { Close(); return; }
    if (closeAt > 0 && now >= closeAt) { Close(); return; }
    if (!userMoved && !dragging) { var np = HudLayout.Place(pid, bornMs, W, H, false, false); if (np != Location) Location = np; }
    Invalidate();
  }

  // ------- pintura -------
  static GraphicsPath RoundedPath(float x, float y, float w, float h, float r) {
    var p = new GraphicsPath(); float d = r * 2;
    p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
    p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
    p.CloseFigure(); return p;
  }
  Dictionary<int, SolidBrush> brushes = new Dictionary<int, SolidBrush>();
  SolidBrush B(Color c) { int k = c.ToArgb(); if (!brushes.ContainsKey(k)) brushes[k] = new SolidBrush(c); return brushes[k]; }

  protected override void OnPaint(PaintEventArgs e) { Render(e.Graphics); }

  public void Render(Graphics g) {
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    var rect = new Rectangle(0, 0, W, H);
    using (var bg = new LinearGradientBrush(rect, Ink1, Ink2, 72f)) g.FillRectangle(bg, rect);
    using (var gp = RoundedPath(0.7f, 0.7f, W - 1.4f, H - 1.4f, RAD))
    using (var pen = new Pen(Color.FromArgb(210, BorderC), 1.3f)) g.DrawPath(pen, gp);

    // cabecalho
    g.DrawString("J.A.R.V.I.S.", fHdr, B(Amber), PAD - 3, 9);
    string st = done ? "CONCLUIDA" : (autoCloseSec > 0 ? "EM 2o PLANO" : "EM CAMPO");
    Color sc = done ? Online : Amber;
    float sw = g.MeasureString(st, fStat).Width;
    float sx = W - 30 - sw;
    using (var b = new SolidBrush(sc)) g.FillEllipse(b, sx - 12, 13, 7, 7);
    g.DrawString(st, fStat, B(sc), sx, 10);
    g.DrawString("x", fClose, B(Faint), closeRect.X + 3, closeRect.Y - 2);

    // titulo tematico (protocolo)
    g.DrawString(Fit(g, Title(), fTitle, W - 2 * PAD), fTitle, B(AmberBright), PAD - 2, 28);

    // enxame (esquerda, animado)
    DrawSwarm(g, SCX, SCY);

    // painel (direita)
    int px = 122;
    g.DrawString("AGENTE", fLbl, B(Faint), px, 52);
    g.DrawString(Fit(g, agent.Length > 0 ? agent : "-", fVal, W - PAD - px), fVal, B(Amber), px, 62);

    g.DrawString("MISSAO", fLbl, B(Faint), px, 84);
    var lines = FitLines(g, task.Length > 0 ? task : "orquestracao de agentes", fBody, W - PAD - px, 2);
    float ty = 94;
    for (int i = 0; i < lines.Count; i++) { g.DrawString(lines[i], fBody, B(TextC), px, ty); ty += 13.5f; }

    // cronometro / metricas
    g.DrawString(done ? "DURACAO" : "EM CURSO", fLbl, B(Faint), px, 128);
    g.DrawString(Elapsed(), fBig, B(done ? Online : Amber), px - 1, 137);
    if (done) {
      string extra = Toks();
      string money = Money();
      if (money.Length > 0) extra = extra.Length > 0 ? extra + "  " + money : money;
      if (extra.Length > 0) g.DrawString(extra, fTiny, B(AmberMut), px + 1, 166);
    } else {
      g.DrawString("o enxame trabalha, senhor", fTiny, B(AmberDeep), px + 1, 166);
    }
    g.DrawImage(Cine.Overlay(W, H), new Rectangle(0, 0, W, H));   // vidro do painel (scanlines + vinheta)
  }

  // ENXAME: nucleo orquestrador + 2 aneis de agentes orbitando + conexoes pulsando.
  // So transform/opacity; repintado no loop de 66ms recortado ao swarmRect.
  void DrawSwarm(Graphics g, float cx, float cy) {
    bool live = !done;
    // aneis-guia faint
    using (var p = new Pen(Color.FromArgb(30, BorderC), 1f)) { g.DrawEllipse(p, cx - 20, cy - 20, 40, 40); g.DrawEllipse(p, cx - 34, cy - 34, 68, 68); }
    float a = (float)(phase * 16.0);
    DrawRing(g, cx, cy, 20f, a, 0f, 3, live);
    DrawRing(g, cx, cy, 34f, -a * 0.8f, 60f, 3, live);
    // nucleo orquestrador
    float pr = 6f + (live ? 1.6f * (float)Math.Sin(phase * 2.3) : 0f);
    using (var gl = new SolidBrush(Color.FromArgb(80, AmberBright))) g.FillEllipse(gl, cx - pr - 5, cy - pr - 5, 2 * pr + 10, 2 * pr + 10);
    using (var b = new SolidBrush(done ? Online : Amber)) g.FillEllipse(b, cx - pr, cy - pr, 2 * pr, 2 * pr);
    using (var b = new SolidBrush(Color.FromArgb(255, 255, 246, 224))) g.FillEllipse(b, cx - 2.4f, cy - 2.4f, 4.8f, 4.8f);
  }

  void DrawRing(Graphics g, float cx, float cy, float R, float aDeg, float offset, int count, bool live) {
    for (int i = 0; i < count; i++) {
      double ang = (aDeg + offset + i * (360.0 / count)) * Math.PI / 180.0;
      float nx = cx + (float)Math.Cos(ang) * R, ny = cy + (float)Math.Sin(ang) * R;
      // conexao centro->no (alfa pulsando)
      int la = (int)(55 + 45 * Math.Sin(phase * 3.0 + i * 1.7));
      if (la < 12) la = 12;
      using (var lp = new Pen(Color.FromArgb(done ? 40 : la, done ? Online : Amber), 1f)) g.DrawLine(lp, cx, cy, nx, ny);
      // pacote de dados viajando na conexao (ordens saindo do orquestrador p/ o agente)
      if (live) {
        double pk = (phase * 0.5 + i * 0.37 + (offset > 0 ? 0.53 : 0)) % 1.0; if (pk < 0) pk += 1;
        float pxp = cx + (float)((nx - cx) * pk), pyp = cy + (float)((ny - cy) * pk);
        using (var pb = new SolidBrush(Color.FromArgb((int)(205 * (1 - pk * 0.55)), AmberBright))) g.FillEllipse(pb, pxp - 1.4f, pyp - 1.4f, 2.8f, 2.8f);
      }
      // no (agente)
      float np = 3.6f + (live ? 1.2f * (float)Math.Sin(phase * 2.5 + i * 1.3) : 0f);
      Color nc = done ? Online : AmberBright;
      using (var gl = new SolidBrush(Color.FromArgb(70, nc))) g.FillEllipse(gl, nx - np - 3, ny - np - 3, 2 * np + 6, 2 * np + 6);
      using (var b = new SolidBrush(nc)) g.FillEllipse(b, nx - np, ny - np, 2 * np, 2 * np);
    }
  }

  string Title() {
    string p = proto.ToUpperInvariant();
    if (p.IndexOf("WORKFLOW") >= 0) return "PROTOCOLO CASA DE FESTAS";
    if (p.IndexOf("PROCESSO") >= 0) return "PROCESSO AUTONOMO";
    if (p.IndexOf("SEGUNDO PLANO") >= 0) return "AGENTE EM SEGUNDO PLANO";
    if (p.IndexOf("SUBAGENTE") >= 0) return "AGENTE EM CAMPO";
    return proto.Length > 0 ? proto : "MISSAO EM CURSO";
  }

  string Elapsed() {
    long endMs = done && doneAt > 0 ? doneAt : NowMs();
    if (start <= 0) return "00:00";
    long s = (endMs - start) / 1000; if (s < 0) s = 0;
    long h = s / 3600, m = (s % 3600) / 60, ss = s % 60;
    return h > 0 ? string.Format("{0}:{1:D2}:{2:D2}", h, m, ss) : string.Format("{0:D2}:{1:D2}", m, ss);
  }

  string Toks() {
    if (tokens <= 0) return "";
    if (tokens >= 1000) return (long)Math.Round(tokens / 1000.0) + "k tokens";
    return tokens + " tokens";
  }

  string Money() {
    if (costUsd <= 0) return "";
    double rate = 5.40;
    try {
      string uf = Path.Combine(dir != null ? Path.Combine(dir, "..") : ".", ".usdbrl.json");
      // .usdbrl.json fica na raiz do jarvis (uma pasta acima de hud\)
      string alt = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "..", ".usdbrl.json");
      string path = File.Exists(alt) ? alt : uf;
      if (File.Exists(path)) { var m = Regex.Match(File.ReadAllText(path), "\"rate\"\\s*:\\s*([\\d.]+)"); if (m.Success) double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out rate); }
    } catch {}
    double brl = costUsd * rate;
    return "R$ " + brl.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
  }

  string Fit(Graphics g, string s, Font f, float max) {
    if (g.MeasureString(s, f).Width <= max) return s;
    while (s.Length > 1 && g.MeasureString(s + "...", f).Width > max) s = s.Substring(0, s.Length - 1);
    return s + "...";
  }
  // quebra em ate n linhas por palavra; a ultima trunca com "..."
  List<string> FitLines(Graphics g, string s, Font f, float max, int n) {
    var outp = new List<string>();
    var words = s.Split(' ');
    string cur = "";
    for (int i = 0; i < words.Length; i++) {
      string test = cur.Length == 0 ? words[i] : cur + " " + words[i];
      if (g.MeasureString(test, f).Width <= max) { cur = test; }
      else {
        if (cur.Length > 0) outp.Add(cur);
        cur = words[i];
        if (outp.Count == n - 1) { outp.Add(Fit(g, cur + " " + string.Join(" ", SubArr(words, i + 1)), f, max)); return outp; }
      }
    }
    if (cur.Length > 0 && outp.Count < n) outp.Add(cur);
    return outp;
  }
  static string[] SubArr(string[] a, int from) { if (from >= a.Length) return new string[0]; var r = new string[a.Length - from]; Array.Copy(a, from, r, 0, r.Length); return r; }

  // dados sinteticos p/ o modo --fanout-shot
  public void Seed() {
    proto = "WORKFLOW MULTI-AGENTE"; agent = "jarvis-upgrade";
    task = "Pesquisa o personagem J.A.R.V.I.S., escreve falas novas e projeta o HUD.";
    start = NowMs() - 14000; statusRaw = "running"; done = false; phase = 0.9;
  }
  void SeedDone() { done = true; statusRaw = "done"; doneAt = start + 754000; tokens = 698568; costUsd = 0.84; phase = 0.4; }

  // ------- interacao -------
  protected override void OnMouseDown(MouseEventArgs e) {
    if (e.Button == MouseButtons.Left) {
      if (closeRect.Contains(e.Location)) { Close(); return; }
      dragging = true; dragStart = e.Location;
    }
    base.OnMouseDown(e);
  }
  protected override void OnMouseMove(MouseEventArgs e) {
    if (dragging) { Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y); movedDuringDrag = true; }
    base.OnMouseMove(e);
  }
  protected override void OnMouseUp(MouseEventArgs e) { if (dragging) { dragging = false; if (movedDuringDrag) { userMoved = true; HudLayout.Release(pid); } movedDuringDrag = false; } base.OnMouseUp(e); }
  protected override void OnFormClosed(FormClosedEventArgs e) { try { HudLayout.Release(pid); } catch {} try { if (mutex != null) mutex.ReleaseMutex(); } catch {} base.OnFormClosed(e); }
}
