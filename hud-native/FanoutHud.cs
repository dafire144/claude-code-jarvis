// J.A.R.V.I.S. — telinha nativa de FAN-OUT (substitui o CMD "Protocolo de Missao").
// Uso: jarvis-hud-wf.exe --fanout <missionFile.json>   (lancada pelo hud-launch.mjs)
//      jarvis-hud-wf.exe --fanout-shot <out.png>        (QA visual, frame sintetico)
// Le o arquivo de missao (hud\<id>.json escrito pelo hud-launch/hud-close):
//   { status, start, proto, agent, task, model, autoCloseSec, doneAt, cost_usd, tokens }
// Mostra o "enxame" trabalhando: nucleo orquestrador + agentes orbitando + conexoes
// pulsando, protocolo, agente, missao, cronometro e (ao concluir) tempo/tokens/custo R$.
// PERFORMANCE: anima a 30fps SO enquanto rodando e SO repinta a area do enxame
// (Invalidate(swarmRect)); dados relidos 1x/s. So transform/opacity no loop -> CPU baixo.
// Timer de ALTA RESOLUCAO (timeBeginPeriod(1)) durante a vida da janela: sem isso o
// WinForms.Timer so entrega quadros em multiplos de ~15.6ms (era o "travado" a 15fps). A
// fase anda pelo RELOGIO (nao por incremento) -> velocidade estavel em qualquer fps, sem
// saltos se um tick atrasar. Mesmas 3 tecnicas do HUD principal (JarvisHudWF.cs).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
  string proto = "", agent = "", task = "", model = "", statusRaw = "running", kind = "";
  long start = 0, doneAt = 0, tokens = 0;
  double costUsd = 0; int autoCloseSec = 0;
  bool done = false;
  long bornMs = NowMs(), closeAt = 0;
  double phase = 0;
  bool dragging; Point dragStart;
  int pid; bool userMoved, movedDuringDrag;   // arrasto manual tira a janela do auto-layout
  WinTimer dataTimer, animTimer, layoutTimer;
  Mutex mutex;
  bool periodSet;
  [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint p);
  [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint p);

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
  static Rectangle minRect = new Rectangle(W - 44, 8, 16, 18);   // minimizar (–), a esquerda do fechar
  static Rectangle swarmRect = new Rectangle(6, 50, 112, 134);
  const int SCX = 62, SCY = 120;   // centro do enxame
  // ---- MINIMIZAR: a telinha de fan-out colapsa numa mini-capsula (enxame + agente + tempo) e
  // reabre no clique -- mesma maquina de morph (reserva-e-cresce) da telinha de sessao. ----
  bool minimized = false, morphing = false; long morphStart = 0; int morphDir = 0;
  Rectangle morphStartRect, morphDestRect;
  Bitmap fullShot = null, miniShotBmp = null;
  const int MINI_W = 182, MINI_H = 54, MORPH_MS = 300;
  static Rectangle miniCloseRect = new Rectangle(MINI_W - 16, 5, 11, 11);   // fechar (mini)
  protected override bool ShowWithoutActivation { get { return true; } }   // reaparecer do "esconder todas" nao rouba o foco
  // SUCÇÃO (v1.6.1): mesmo contrato da telinha de sessao (voa ate o botao / volta dele)
  int hideFly = 0; long hideFlyT0 = 0; Point hideFlyFrom;
  Point BtnPoint() {
    Rectangle b;
    if (HudLayout.ReadBtnPos(out b)) return new Point(b.X + b.Width - Width, b.Y);
    var wa = Screen.PrimaryScreen.WorkingArea; return new Point(wa.Right - Width - 12, wa.Top + 42);
  }
  int CurW() { return minimized ? MINI_W : W; }
  int CurH() { return minimized ? MINI_H : H; }

  public static void Run(string missionFile) {
    string id = Path.GetFileNameWithoutExtension(missionFile);
    bool made; var m = new Mutex(true, "JarvisFanout_" + id, out made);
    if (!made) return;                       // ja existe janela p/ essa missao
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new FanoutHud(missionFile, m));
  }

  public static void Shot(string outPng) { Shot(outPng, false, "", false); }
  public static void Shot(string outPng, bool asDone) { Shot(outPng, asDone, "", false); }
  public static void Shot(string outPng, bool asDone, string kindArg) { Shot(outPng, asDone, kindArg, false); }
  public static void Shot(string outPng, bool asDone, string kindArg, bool asMini) {
    var f = new FanoutHud(null, null);
    f.Seed(); f.kind = kindArg;
    if (kindArg == "qa") { f.proto = "AUDITORIA DE QUALIDADE"; f.agent = "orna-qa"; f.task = "Inspecao de qualidade do modulo, antes da entrega ao senhor."; f.phase = 5.7; }
    else if (kindArg == "qa_ultra") { f.proto = "AUDITORIA PROFUNDA"; f.agent = "orna-qa-ultra"; f.task = "Auditoria multiagente do dock: correcao, concorrencia e paridade."; f.phase = 2.2; }
    if (asDone) f.SeedDone();
    int bw = asMini ? MINI_W : W, bh = asMini ? MINI_H : H;
    var bmp = new Bitmap(bw, bh);
    using (var g = Graphics.FromImage(bmp)) { if (asMini) f.RenderMini(g); else f.Render(g); }
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

    // timer de ALTA RESOLUCAO durante a vida da janela (mesmo motivo do HUD principal): sem
    // isso o WinForms.Timer so entrega quadros em multiplos de ~15.6ms -> era o jitter que
    // travava o enxame a 15fps. Com 1ms, o passo de 33ms sai regular e liso. Devolvido no close.
    try { timeBeginPeriod(1); periodSet = true; } catch {}

    pid = System.Diagnostics.Process.GetCurrentProcess().Id;
    ReadMission();
    if (HudLayout.WantStartMinimized()) {                   // config compartilhada: a fan-out tambem nasce mini-capsula no dock
      minimized = true;
      ClientSize = new Size(MINI_W, MINI_H);
      try { using (var gpm = RoundedPath(0, 0, MINI_W, MINI_H, RegionRad(MINI_H))) Region = new Region(gpm); } catch {}
      Location = HudLayout.Place(pid, bornMs, MINI_W, MINI_H, false, true);
    } else {
      Location = HudLayout.Place(pid, bornMs, W, H, false, false);
    }

    dataTimer = new WinTimer(); dataTimer.Interval = 1000;
    dataTimer.Tick += delegate { DataTick(); };
    dataTimer.Start();

    layoutTimer = new WinTimer(); layoutTimer.Interval = 60;   // reflow rapido no dock (~16x/s, paridade com a telinha de sessao)
    layoutTimer.Tick += delegate { PlaceTick(); };
    layoutTimer.Start();

    animTimer = new WinTimer(); animTimer.Interval = 33;   // 30fps: divisor exato de 60Hz -> sem judder
    // fase pelo RELOGIO (nao por incremento): velocidade estavel em qualquer fps, sem "saltos"
    // se um tick atrasar. Divisor 471 preserva a cadencia anterior (0.14/66ms ~= 2.1 voltas/s).
    animTimer.Tick += delegate {
      phase = (NowMs() - bornMs) / 471.0;
      if (morphing) {
        double mt = (NowMs() - morphStart) / (double)MORPH_MS;
        if (mt >= 1) { EndMorph(); return; }
        ApplyMorphBounds(mt); Invalidate(); return;
      }
      if (hideFly == 1 || hideFly == 3) {             // SUCÇÃO pro botao / retorno (one-shot ~280ms a ~120fps)
        double ht = (NowMs() - hideFlyT0) / 280.0; if (ht > 1) ht = 1;
        // succao ACELERA pro botao (easeIn); retorno DESACELERA ao pousar (easeOut)
        double hs = hideFly == 1 ? ht * ht : 1 - (1 - ht) * (1 - ht);
        Point A = hideFly == 1 ? hideFlyFrom : BtnPoint();
        Point B = hideFly == 1 ? BtnPoint() : hideFlyFrom;
        Location = new Point((int)Math.Round(A.X + (B.X - A.X) * hs), (int)Math.Round(A.Y + (B.Y - A.Y) * hs));
        try { Opacity = hideFly == 1 ? 1 - hs : hs; } catch { /* ok */ }
        if (ht >= 1) {
          try { animTimer.Interval = 33; } catch { /* ok */ }   // devolve o passo normal
          if (hideFly == 1) { Hide(); try { Opacity = 1; } catch { /* ok */ } Location = hideFlyFrom; hideFly = 2; }
          else { try { Opacity = 1; } catch { /* ok */ } hideFly = 0; }
        }
        return;
      }
      if (minimized) { Invalidate(); return; }   // mini e pequeno: repinta tudo (barato)
      Invalidate(swarmRect);
    };
    if (!done) animTimer.Start();
  }

  static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

  // parse simples (regex) do JSON de missao escrito pelo hud-launch/hud-close
  void ReadMission() {
    try {
      if (file == null || !File.Exists(file)) return;
      string s = File.ReadAllText(file);
      proto = Grp(s, "\"proto\":\"([^\"]*)\"");
      kind = Grp(s, "\"kind\":\"([^\"]*)\"");   // "qa" (inspecao simples) | "qa_ultra" (auditoria multi-agente) | ""
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
    HudLayout.EnsureMinAllButton();   // o botao flutuante "minimizar todas" vive enquanto houver telinha
    ReadMission();
    long now = NowMs();
    // define quando fechar
    if (done) {
      // mantem o animTimer vivo (custo minimo) p/ o morph/mini funcionarem mesmo apos concluir
      if (closeAt == 0) { closeAt = (doneAt > 0 ? doneAt : now) + 8000; Invalidate(); }
    } else if (autoCloseSec > 0 && start > 0 && now - start > (long)autoCloseSec * 1000) {
      // processo em 2o plano sem sinal de fim: encerra o painel informativo
      if (closeAt == 0) closeAt = now + 200;
    }
    // trava de seguranca: nunca vive mais de 20 min
    if (now - bornMs > 20 * 60 * 1000) { Close(); return; }
    // arquivo de missao sumiu (faxina) -> encerra
    if (file != null && !File.Exists(file) && now - bornMs > 5000) { Close(); return; }
    if (closeAt > 0 && now >= closeAt) { Close(); return; }
    PlaceTick();
    Invalidate();
  }

  // reflow rapido no dock (~60ms): a casa de festas reencaixa junto das telinhas de sessao
  void PlaceTick() {
    // "ESCONDER TODAS" (botao flutuante) com SUCÇÃO: mesmo contrato da telinha de sessao
    bool esconder = HudLayout.IsHidden();
    if (esconder && hideFly == 0 && Visible && !morphing) {
      hideFly = 1; hideFlyT0 = NowMs(); hideFlyFrom = Location;
      try { animTimer.Interval = 8; } catch { /* ok */ }        // voo a ~120fps (padrao dos one-shots)
    }
    if (!esconder && hideFly == 2) {
      hideFly = 3; hideFlyT0 = NowMs();
      try { Opacity = 0; } catch { /* ok */ }
      try { animTimer.Interval = 8; } catch { /* ok */ }
      Location = BtnPoint(); Show();
    }
    if (hideFly != 0) { if (!userMoved) HudLayout.Touch(pid); return; }   // voo/escondida: o animTimer conduz
    if (!Visible) Show();
    if (userMoved) return;
    if (dragging || morphing) { HudLayout.Touch(pid); return; }   // usuario/morph controlam a posicao: so renova o hb
    var np = HudLayout.Place(pid, bornMs, CurW(), CurH(), false, minimized);
    if (np != Location) Location = np;
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

  protected override void OnPaint(PaintEventArgs e) {
    if (morphing) { PaintMorph(e.Graphics); return; }      // colapso/expansao da mini-capsula
    if (minimized) { RenderMini(e.Graphics); return; }     // mini-capsula no dock
    Render(e.Graphics);
  }

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
    float sx = W - 52 - sw;   // recuado p/ abrir espaco aos 2 botoes (minimizar/fechar)
    using (var b = new SolidBrush(sc)) g.FillEllipse(b, sx - 12, 13, 7, 7);
    g.DrawString(st, fStat, B(sc), sx, 10);
    // botoes RECOLHER TUDO (chevron duplo), MINIMIZAR (–) e FECHAR (x), em ambar visivel
    using (var mp = new Pen(AmberMut, 2f)) { mp.StartCap = LineCap.Round; mp.EndCap = LineCap.Round; g.DrawLine(mp, minRect.X + 3, minRect.Y + 9, minRect.X + 13, minRect.Y + 9); }
    g.DrawString("x", fClose, B(AmberMut), closeRect.X + 3, closeRect.Y - 2);

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
      string sub = kind == "qa_ultra" ? "a banca audita, senhor" : kind == "qa" ? "o inspetor examina, senhor" : "o enxame trabalha, senhor";
      g.DrawString(sub, fTiny, B(AmberDeep), px + 1, 166);
    }
    g.DrawImage(Cine.Overlay(W, H), new Rectangle(0, 0, W, H));   // vidro do painel (scanlines + vinheta)
  }

  // ENXAME: nucleo orquestrador + 2 aneis de agentes orbitando + conexoes pulsando.
  // So transform/opacity; repintado no loop de 33ms recortado ao swarmRect.
  // Para o AGENTE DE QA a arte muda: banca de auditores sob varredura de radar (qa_ultra) ou
  // um inspetor de lupa passando uma grade de codigo (qa) -- animacoes proprias da auditoria.
  void DrawSwarm(Graphics g, float cx, float cy) {
    if (kind == "qa_ultra") { DrawAuditSwarm(g, cx, cy); return; }
    if (kind == "qa") { DrawInspector(g, cx, cy); return; }
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

  // ---- AGENTE DE QA: animacoes proprias ----
  // qa_ultra: BANCA DE AUDITORES (aneis de nos) sob uma VARREDURA DE RADAR girando.
  void DrawAuditSwarm(Graphics g, float cx, float cy) {
    bool live = !done;
    using (var p = new Pen(Color.FromArgb(30, BorderC), 1f)) { g.DrawEllipse(p, cx - 20, cy - 20, 40, 40); g.DrawEllipse(p, cx - 34, cy - 34, 68, 68); }
    float a = (float)(phase * 12.0);
    DrawRing(g, cx, cy, 20f, a, 0f, 3, live);
    DrawRing(g, cx, cy, 34f, -a * 0.8f, 60f, 3, live);
    float pr = 6f + (live ? 1.4f * (float)Math.Sin(phase * 2.3) : 0f);   // nucleo: o sintetizador-chefe
    using (var gl = new SolidBrush(Color.FromArgb(80, AmberBright))) g.FillEllipse(gl, cx - pr - 5, cy - pr - 5, 2 * pr + 10, 2 * pr + 10);
    using (var b = new SolidBrush(done ? Online : Amber)) g.FillEllipse(b, cx - pr, cy - pr, 2 * pr, 2 * pr);
    using (var b = new SolidBrush(Color.FromArgb(255, 255, 246, 224))) g.FillEllipse(b, cx - 2.4f, cy - 2.4f, 4.8f, 4.8f);
    if (live) DrawRadarSweep(g, cx, cy, 40f);
    else DrawCheck(g, cx, cy, 9f, Online);   // auditoria concluida: selo de aprovacao no centro
  }

  // feixe de radar: cunha translucida atras + linha brilhante na borda de ataque, girando
  void DrawRadarSweep(Graphics g, float cx, float cy, float R) {
    double sweep = (phase * 1.4) % (2 * Math.PI); if (sweep < 0) sweep += 2 * Math.PI;
    float deg = (float)(sweep * 180.0 / Math.PI);
    using (var gp = new GraphicsPath()) {
      gp.AddPie(cx - R, cy - R, 2 * R, 2 * R, deg - 44, 44);
      using (var pgb = new PathGradientBrush(gp)) {
        pgb.CenterPoint = new PointF(cx, cy);
        pgb.CenterColor = Color.FromArgb(78, AmberBright);
        pgb.SurroundColors = new Color[] { Color.FromArgb(0, Amber) };
        try { g.FillPath(pgb, gp); } catch {}
      }
    }
    float ex = cx + (float)(Math.Cos(sweep) * R), ey = cy + (float)(Math.Sin(sweep) * R);
    using (var pen = new Pen(Color.FromArgb(210, 255, 246, 224), 1.6f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; g.DrawLine(pen, cx, cy, ex, ey); }
    using (var b = new SolidBrush(Color.FromArgb(230, AmberBright))) g.FillEllipse(b, ex - 2f, ey - 2f, 4f, 4f);
  }

  // qa: INSPETOR — uma lupa percorre uma grade de "celulas de codigo"; cada uma vira ✓ ao ser aferida.
  void DrawInspector(Graphics g, float cx, float cy) {
    int cols = 4, rows = 3; float cell = 15f, gap = 5f;
    float gw = cols * cell + (cols - 1) * gap, gh = rows * cell + (rows - 1) * gap;
    float gx = cx - gw / 2f, gy = cy - gh / 2f;
    int total = cols * rows;
    long step = (long)Math.Floor(phase * 0.7);
    int scanned = done ? total : (int)(step % (total + 4));   // percorre as celulas, pausa "tudo ok", reinicia
    if (scanned > total) scanned = total;
    for (int r = 0; r < rows; r++) {
      for (int c = 0; c < cols; c++) {
        int idx = r * cols + c;
        float x = gx + c * (cell + gap), y = gy + r * (cell + gap);
        bool ok = idx < scanned;
        Color cc = ok ? Online : AmberDeep;
        using (var b = new SolidBrush(Color.FromArgb(ok ? 40 : 20, cc))) g.FillRectangle(b, x, y, cell, cell);
        using (var pen = new Pen(Color.FromArgb(ok ? 170 : 70, cc), 1f)) g.DrawRectangle(pen, x, y, cell, cell);
        if (ok) DrawCheck(g, x + cell / 2f, y + cell / 2f, cell * 0.42f, Online);
      }
    }
    if (done) DrawLens(g, gx + gw + 4f, gy + gh + 2f);                 // concluido: lupa recolhida
    else { int cur = scanned < total ? scanned : total - 1; DrawLens(g, gx + (cur % cols) * (cell + gap) + cell / 2f, gy + (cur / cols) * (cell + gap) + cell / 2f); }
  }

  // lupa: halo + lente translucida + aro + cabo
  void DrawLens(Graphics g, float lx, float ly) {
    float lr = 10.5f;
    using (var gl = new SolidBrush(Color.FromArgb(60, AmberBright))) g.FillEllipse(gl, lx - lr - 2, ly - lr - 2, 2 * lr + 4, 2 * lr + 4);
    using (var glass = new SolidBrush(Color.FromArgb(30, 255, 246, 224))) g.FillEllipse(glass, lx - lr, ly - lr, 2 * lr, 2 * lr);
    using (var pen = new Pen(Color.FromArgb(235, AmberBright), 2f)) g.DrawEllipse(pen, lx - lr, ly - lr, 2 * lr, 2 * lr);
    using (var pen = new Pen(Color.FromArgb(235, AmberMut), 2.4f)) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; double ang = 0.85; g.DrawLine(pen, lx + (float)(Math.Cos(ang) * lr), ly + (float)(Math.Sin(ang) * lr), lx + (float)(Math.Cos(ang) * (lr + 7)), ly + (float)(Math.Sin(ang) * (lr + 7))); }
  }

  // check-mark desenhado com 2 tracos (usado na grade e no selo final)
  void DrawCheck(Graphics g, float cx, float cy, float r, Color col) {
    using (var pen = new Pen(col, Math.Max(1.4f, r * 0.28f))) {
      pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
      var pts = new PointF[] { new PointF(cx - r * 0.75f, cy + r * 0.05f), new PointF(cx - r * 0.15f, cy + r * 0.65f), new PointF(cx + r * 0.8f, cy - r * 0.7f) };
      g.DrawLines(pen, pts);
    }
  }

  string Title() {
    if (kind == "qa_ultra") return "AUDITORIA PROFUNDA";
    if (kind == "qa") return "INSPECAO DE QUALIDADE";
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
      if (morphing || hideFly != 0) return;                  // durante o morph/succao, ignora cliques
      if (minimized) {
        if (miniCloseRect.Contains(e.Location)) { Close(); return; }
        dragging = true; dragStart = e.Location;              // clique simples restaura (decidido no MouseUp)
        base.OnMouseDown(e); return;
      }
      if (closeRect.Contains(e.Location)) { Close(); return; }
      if (minRect.Contains(e.Location)) { BeginMorph(true); return; }   // minimizar
      dragging = true; dragStart = e.Location;
    }
    base.OnMouseDown(e);
  }
  protected override void OnMouseMove(MouseEventArgs e) {
    if (dragging) {
      int dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
      // WM_MOUSEMOVE espurio de delta ZERO apos o mouse-down nao e arrasto (ver JarvisHudWF)
      if (dx != 0 || dy != 0) { Location = new Point(Location.X + dx, Location.Y + dy); movedDuringDrag = true; }
    }
    base.OnMouseMove(e);
  }
  protected override void OnMouseUp(MouseEventArgs e) {
    if (dragging) {
      dragging = false;
      if (movedDuringDrag) { userMoved = true; HudLayout.Release(pid); }
      else if (minimized && !morphing) { BeginMorph(false); }   // clique simples na mini -> restaura
      movedDuringDrag = false;
    }
    base.OnMouseUp(e);
  }
  // ------- MINIMIZAR: morph (reserva-e-cresce, mesma logica do HUD principal) + mini-capsula -------
  static double SmoothStep(double t) { if (t < 0) t = 0; if (t > 1) t = 1; return t * t * (3 - 2 * t); }
  float RegionRad(int h) { double p = (double)(H - h) / (H - MINI_H); if (p < 0) p = 0; if (p > 1) p = 1; return (float)(RAD + (MINI_H / 2.0 - RAD) * p); }
  static ColorMatrix AlphaMatrix(double a) { if (a < 0) a = 0; if (a > 1) a = 1; var m = new ColorMatrix(); m.Matrix33 = (float)a; return m; }

  void BeginMorph(bool toMini) {
    if (morphing) return;
    bool wasDragged = userMoved;
    morphing = true; morphDir = toMini ? 1 : -1; morphStart = NowMs();
    morphStartRect = Bounds;
    if (toMini) userMoved = false;                                        // minimizar rejunta ao dock
    if (toMini && wasDragged) {
      Point p = HudLayout.Place(pid, bornMs, MINI_W, MINI_H, false, true);   // arrastada: entra JA como mini (sem solavanco duplo)
      morphDestRect = new Rectangle(p.X, p.Y, MINI_W, MINI_H);
    } else {
      Point anchor;
      if (!userMoved && !dragging) anchor = HudLayout.Place(pid, bornMs, W, H, false, false);   // reserva a pegada CHEIA no dock
      else anchor = new Point(Location.X + Width - W, Location.Y);
      morphDestRect = toMini ? new Rectangle(anchor.X + W - MINI_W, anchor.Y, MINI_W, MINI_H)
                             : new Rectangle(anchor.X, anchor.Y, W, H);
    }
    try { fullShot = new Bitmap(W, H); using (var g = Graphics.FromImage(fullShot)) Render(g); } catch { fullShot = null; }
    try { miniShotBmp = new Bitmap(MINI_W, MINI_H); using (var g = Graphics.FromImage(miniShotBmp)) RenderMini(g); } catch { miniShotBmp = null; }
    if (animTimer != null) { animTimer.Interval = 8; if (!animTimer.Enabled) animTimer.Start(); }   // morph liso; garante rodando mesmo apos concluir
  }
  void ApplyMorphBounds(double t) {
    double e = SmoothStep(t);
    int x = (int)Math.Round(morphStartRect.X + (morphDestRect.X - morphStartRect.X) * e);
    int y = (int)Math.Round(morphStartRect.Y + (morphDestRect.Y - morphStartRect.Y) * e);
    int w = (int)Math.Round(morphStartRect.Width + (morphDestRect.Width - morphStartRect.Width) * e);
    int h = (int)Math.Round(morphStartRect.Height + (morphDestRect.Height - morphStartRect.Height) * e);
    try { SetBounds(x, y, w, h); using (var gp = RoundedPath(0, 0, w, h, RegionRad(h))) Region = new Region(gp); } catch {}
  }
  void EndMorph() {
    morphing = false; minimized = (morphDir == 1);
    int w = CurW(), h = CurH();
    int nx = morphDestRect.X, ny = morphDestRect.Y;
    if (!userMoved && !dragging) { var np = HudLayout.Place(pid, bornMs, w, h, false, minimized); nx = np.X; ny = np.Y; }
    try { SetBounds(nx, ny, w, h); using (var gp = RoundedPath(0, 0, w, h, RegionRad(h))) Region = new Region(gp); } catch {}
    try { if (fullShot != null) { fullShot.Dispose(); fullShot = null; } } catch {}
    try { if (miniShotBmp != null) { miniShotBmp.Dispose(); miniShotBmp = null; } } catch {}
    if (animTimer != null) animTimer.Interval = 33;   // regime normal (mini e cheio animam a 30fps)
    Invalidate();
  }
  void PaintMorph(Graphics g) {
    double t = (NowMs() - morphStart) / (double)MORPH_MS; if (t < 0) t = 0; if (t > 1) t = 1;
    double p = morphDir == 1 ? SmoothStep(t) : SmoothStep(1 - t);
    int w = Width, h = Height;
    g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.Bilinear;
    using (var bgb = new SolidBrush(Ink2)) using (var gp = RoundedPath(0, 0, w, h, RegionRad(h))) g.FillPath(bgb, gp);
    if (fullShot != null && p < 0.98) using (var ia = new ImageAttributes()) { ia.SetColorMatrix(AlphaMatrix(1 - p)); g.DrawImage(fullShot, new Rectangle(0, 0, w, h), 0, 0, W, H, GraphicsUnit.Pixel, ia); }
    if (miniShotBmp != null && p > 0.02) using (var ia = new ImageAttributes()) { ia.SetColorMatrix(AlphaMatrix(p)); g.DrawImage(miniShotBmp, new Rectangle(0, 0, w, h), 0, 0, MINI_W, MINI_H, GraphicsUnit.Pixel, ia); }
  }

  // titulo curto p/ a mini-capsula (quando nao ha nome de agente)
  string MiniTitle() {
    if (kind == "qa_ultra") return "AUDITORIA";
    if (kind == "qa") return "INSPECAO";
    string p = proto.ToUpperInvariant();
    if (p.IndexOf("WORKFLOW") >= 0) return "CASA DE FESTAS";
    if (p.IndexOf("SEGUNDO PLANO") >= 0) return "EM 2o PLANO";
    if (p.IndexOf("SUBAGENTE") >= 0) return "AGENTE";
    if (p.IndexOf("PROCESSO") >= 0) return "PROCESSO";
    return "MISSAO";
  }
  // MINI-CAPSULA do fan-out: enxame compacto a esquerda + agente (ou protocolo) + status/tempo.
  void RenderMini(Graphics g) {
    g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    var rect = new Rectangle(0, 0, MINI_W, MINI_H);
    using (var bg = new LinearGradientBrush(rect, Ink1, Ink2, 60f)) using (var gp = RoundedPath(0, 0, MINI_W, MINI_H, MINI_H / 2f)) g.FillPath(bg, gp);
    using (var gp = RoundedPath(0.7f, 0.7f, MINI_W - 1.4f, MINI_H - 1.4f, (MINI_H - 1.4f) / 2f))
    using (var pen = new Pen(Color.FromArgb(210, BorderC), 1.3f)) g.DrawPath(pen, gp);
    DrawMiniSwarm(g, 27, MINI_H / 2f);
    float tx = 52;
    Color sc = done ? Online : Amber;
    string t1 = agent.Length > 0 ? agent : MiniTitle();
    g.DrawString(Fit(g, t1, fStat, MINI_W - tx - 20), fStat, B(AmberBright), tx, 9);   // recuado p/ o botao fechar
    string t2 = (done ? "CONCLUIDA" : "EM CURSO") + "  " + Elapsed();
    g.DrawString(Fit(g, t2, fTiny, MINI_W - tx - 12), fTiny, B(sc), tx, 27);
    g.DrawString("x", fTiny, B(AmberMut), miniCloseRect.X + 1, miniCloseRect.Y - 2);
    g.DrawImage(Cine.Overlay(MINI_W, MINI_H), rect);
  }
  // enxame compacto (le bem em ~44px): 1 anel de 3 nos girando + nucleo pulsando
  void DrawMiniSwarm(Graphics g, float cx, float cy) {
    bool live = !done;
    float a = (float)(phase * 16.0);
    using (var p = new Pen(Color.FromArgb(40, BorderC), 1f)) g.DrawEllipse(p, cx - 15, cy - 15, 30, 30);
    for (int i = 0; i < 3; i++) {
      double ang = (a + i * 120.0) * Math.PI / 180.0;
      float nx = cx + (float)Math.Cos(ang) * 15, ny = cy + (float)Math.Sin(ang) * 15;
      using (var lp = new Pen(Color.FromArgb(done ? 40 : 70, done ? Online : Amber), 1f)) g.DrawLine(lp, cx, cy, nx, ny);
      float np = 2.4f + (live ? 0.8f * (float)Math.Sin(phase * 2.5 + i * 1.3) : 0f);
      Color nc = done ? Online : AmberBright;
      using (var b = new SolidBrush(nc)) g.FillEllipse(b, nx - np, ny - np, 2 * np, 2 * np);
    }
    float pr = 4f + (live ? 1f * (float)Math.Sin(phase * 2.3) : 0f);
    using (var gl = new SolidBrush(Color.FromArgb(80, AmberBright))) g.FillEllipse(gl, cx - pr - 3, cy - pr - 3, 2 * pr + 6, 2 * pr + 6);
    using (var b = new SolidBrush(done ? Online : Amber)) g.FillEllipse(b, cx - pr, cy - pr, 2 * pr, 2 * pr);
    using (var b = new SolidBrush(Color.FromArgb(255, 255, 246, 224))) g.FillEllipse(b, cx - 1.6f, cy - 1.6f, 3.2f, 3.2f);
  }

  protected override void OnFormClosed(FormClosedEventArgs e) {
    try { if (fullShot != null) { fullShot.Dispose(); fullShot = null; } } catch {}
    try { if (miniShotBmp != null) { miniShotBmp.Dispose(); miniShotBmp = null; } } catch {}
    try { if (periodSet) { timeEndPeriod(1); periodSet = false; } } catch {}
    try { HudLayout.Release(pid); } catch {}
    try { if (mutex != null) mutex.ReleaseMutex(); } catch {}
    base.OnFormClosed(e);
  }
}
