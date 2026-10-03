#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MountainDash
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new GameForm());
        }
    }

    public class GameForm : Form
    {
        // ---------- Tuning (change these to adjust difficulty) ----------
        const double G = 900;            // gravity
        const double Accel = 820;        // engine power
        const double BrakeForce = 900;   // brake strength
        const double MaxSpeed = 1000;    // top speed
        const double SummitX = 24000;    // distance to the top (bigger = longer climb)
        const double MaxFuel = 75;
        const double FuelPerCan = 35;
        const double GasBurn = 3.2;      // fuel/sec while on the gas
        const double IdleBurn = 0.25;    // fuel/sec otherwise
        const int CanCount = 15;
        const double CanSpacing = 1500;
        const double CanStart = 1200;

        // ---------- Truck geometry ----------
        const double R = 17;             // wheel radius
        const double HalfBase = 46;      // half the distance between wheels

        const double Step = 1.0 / 120.0; // physics step

        enum State { Title, Playing, Crashed, NoFuel, Won }
        State state = State.Title;

        // truck state
        double cx, cy, angle, angVel, v, vx, vy, gvy, wheelRot;
        bool airborne;

        // game state
        double fuel, time, best, emptyTimer, acc;
        bool gas, brake;
        bool[] gotCan = new bool[CanCount];
        double camX, camY;

        HashSet<Keys> keys = new HashSet<Keys>();
        Stopwatch sw = Stopwatch.StartNew();
        double lastT;
        System.Windows.Forms.Timer timer;

        Font hudFont = new Font("Segoe UI", 13, FontStyle.Bold);
        Font smallFont = new Font("Segoe UI", 10, FontStyle.Bold);
        Font bigFont = new Font("Segoe UI", 54, FontStyle.Bold | FontStyle.Italic);
        Font medFont = new Font("Segoe UI", 18, FontStyle.Bold);



        public GameForm()
        {
            Text = "Mountain Dash";
            ClientSize = new Size(1000, 600);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            Reset();
            state = State.Title;

            timer = new System.Windows.Forms.Timer { Interval = 15 };
            timer.Tick += (s, e) =>
            {
                double now = sw.Elapsed.TotalSeconds;
                double dt = Math.Min(now - lastT, 1.0 / 20);
                lastT = now;
                UpdateGame(dt);
                Invalidate();
            };
            lastT = sw.Elapsed.TotalSeconds;
            timer.Start();
        }

        // ================= TERRAIN =================
        static double Terrain(double x)
        {
            double ramp = Math.Min(1.0, x / 2000.0); // gentle start
            double bumps = 40 * Math.Sin(x * 0.008) + 18 * Math.Sin(x * 0.02 + 2) + 6 * Math.Sin(x * 0.05);
            return 600 - 0.2 * x + bumps * ramp;
        }

        static double GroundAngle(double x)
        {
            double rx = x - HalfBase, fx = x + HalfBase;
            return Math.Atan2(Terrain(fx) - Terrain(rx), fx - rx);
        }

        // Finds the truck's angle and center height when both wheels sit on the ground at x
        static void Solve(double x, double a0, out double a, out double y)
        {
            a = a0;
            for (int i = 0; i < 3; i++)
            {
                double dx = HalfBase * Math.Cos(a);
                a = Math.Atan2(Terrain(x + dx) - Terrain(x - dx), 2 * dx);
            }
            double d = HalfBase * Math.Cos(a);
            y = (Terrain(x - d) + Terrain(x + d)) / 2 - R;
        }

        static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        static double Clamp(double x, double lo, double hi) { return Math.Max(lo, Math.Min(hi, x)); }

        // ================= GAME LOGIC =================
        void Reset()
        {
            cx = 200;
            angle = GroundAngle(cx);
            Solve(cx, angle, out double a, out double y);
            angle = a; cy = y;
            angVel = 0; v = 0; vx = 0; vy = 0; gvy = 0; wheelRot = 0;
            airborne = false;
            fuel = MaxFuel; time = 0; emptyTimer = 0; acc = 0;
            gotCan = new bool[CanCount];
            camX = cx - ClientSize.Width * 0.3;
            camY = cy - ClientSize.Height * 0.6;
        }

        void StartRun()
        {
            Reset();
            state = State.Playing;
        }

        void UpdateGame(double dt)
        {
            int W = ClientSize.Width, H = ClientSize.Height;

            if (state == State.Playing)
            {
                time += dt;

                gas = (keys.Contains(Keys.Right) || keys.Contains(Keys.D)) && fuel > 0;
                brake = keys.Contains(Keys.Left) || keys.Contains(Keys.A);

                fuel -= dt * (gas ? GasBurn : IdleBurn);
                if (fuel < 0) fuel = 0;
                if (fuel <= 0) emptyTimer += dt; else emptyTimer = 0;

                acc += dt;
                while (acc >= Step && state == State.Playing)
                {
                    Physics(Step);
                    acc -= Step;
                }

                // fuel pickups
                for (int i = 0; i < CanCount; i++)
                {
                    if (gotCan[i]) continue;
                    double px = CanStart + i * CanSpacing;
                    double py = Terrain(px) - 30;
                    if (Math.Abs(cx - px) < 70 && Math.Abs(cy - py) < 120)
                    {
                        gotCan[i] = true;
                        fuel = Math.Min(MaxFuel, fuel + FuelPerCan);
                    }
                }

                if (state == State.Playing && cx >= SummitX)
                {
                    state = State.Won;
                    if (best == 0 || time < best) best = time;
                }
                if (state == State.Playing && emptyTimer > 3.5 && !airborne)
                    state = State.NoFuel;
            }

            // camera
            double tx = cx - W * 0.3 + v * 0.1;
            double ty = cy - H * 0.6;
            double k = Math.Min(1, 5 * dt);
            camX += (tx - camX) * k;
            camY += (ty - camY) * k;
        }

        void Physics(double dt)
        {
            if (!airborne)
            {
                if (gas) v += Accel * dt;
                if (brake)
                {
                    if (v > 10) v -= BrakeForce * dt;
                    else v -= 350 * dt; // reverse
                }
                v += G * Math.Sin(angle) * dt;   // gravity along the slope
                v -= v * 0.35 * dt;              // drag
                v = Clamp(v, -250, MaxSpeed);

                double nx = cx + v * Math.Cos(angle) * dt;
                if (nx < 80) { nx = 80; if (v < 0) v = 0; }

                Solve(nx, angle, out double na, out double ny);
                double nvy = (ny - cy) / dt;
                double accelY = (nvy - gvy) / dt;

                // Ground drops away faster than gravity -> we take off!
                if (v > 200 && accelY > G * 1.15)
                {
                    airborne = true;
                    vx = (nx - cx) / dt;
                    vy = nvy;
                    angVel = Clamp((na - angle) / dt, -3, 3);
                }

                cx = nx; cy = ny; angle = na; gvy = nvy;
                wheelRot += v / R * dt;
            }
            else
            {
                vy += G * dt;
                if (gas) angVel -= 5 * dt;   // nose up
                if (brake) angVel += 5 * dt; // nose down
                angVel = Clamp(angVel, -4, 4);

                cx += vx * dt;
                cy += vy * dt;
                angle = Wrap(angle + angVel * dt);
                wheelRot += vx / R * dt * 0.5;

                double c = Math.Cos(angle), s = Math.Sin(angle);
                double rx = cx - HalfBase * c, ry = cy - HalfBase * s;
                double fx = cx + HalfBase * c, fy = cy + HalfBase * s;
                if (ry + R >= Terrain(rx) || fy + R >= Terrain(fx))
                    Land();
            }
        }

        void Land()
        {
            double ga = GroundAngle(cx);
            double diff = Math.Abs(Wrap(angle - ga));
            if (diff > 0.9) // landed on roof / too steep
            {
                state = State.Crashed;
                return;
            }
            Solve(cx, ga, out double na, out double ny);
            angle = na; cy = ny;
            v = Clamp(vx * Math.Cos(angle) + vy * Math.Sin(angle), -250, MaxSpeed);
            gvy = v * Math.Sin(angle);
            angVel = 0;
            airborne = false;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            keys.Clear();
            base.OnDeactivate(e);
        }

        // ================= INPUT =================
        protected override void OnKeyDown(KeyEventArgs e)
        {
            keys.Add(e.KeyCode);
            if (e.KeyCode == Keys.Escape) Close();
            if (state != State.Playing && e.KeyCode == Keys.Enter) StartRun();
            if (e.KeyCode == Keys.R) StartRun();
            // Jump: space / up / W -- only while playing and on the ground
            if (state == State.Playing && !airborne && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Up || e.KeyCode == Keys.W))
            {
                // give an upward impulse and mark airborne
                vy -= 420; // negative = upward
                vx = v * Math.Cos(angle);
                angVel = -1.2; // tilt nose up a bit
                airborne = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            keys.Remove(e.KeyCode);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = ClientSize.Width, H = ClientSize.Height;
            double p = Clamp(cx / SummitX, 0, 1);

            DrawSky(g, W, H, p);
            DrawFarMountains(g, W, H, p);
            DrawTerrain(g, W, H, p);
            DrawSigns(g, W);
            DrawCans(g, W);
            DrawTruck(g);
            DrawHud(g, W, H, p);

            if (state != State.Playing) DrawOverlay(g, W, H);
        }

        // ================= DRAWING =================
        static Color Lerp(Color a, Color b, double t)
        {
            t = Clamp(t, 0, 1);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        void DrawSky(Graphics g, int W, int H, double p)
        {
            Color top = Lerp(Color.FromArgb(70, 140, 230), Color.FromArgb(15, 25, 80), p);
            Color bot = Lerp(Color.FromArgb(190, 225, 250), Color.FromArgb(110, 140, 200), p);
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, W, H + 1), top, bot, 90f))
                g.FillRectangle(br, 0, 0, W, H);

            // sun
            using (var br = new SolidBrush(Color.FromArgb(230, 255, 240, 170)))
                g.FillEllipse(br, W - 150, 60, 70, 70);
        }

        void DrawFarMountains(Graphics g, int W, int H, double p)
        {
            double[] par = { 0.05, 0.12, 0.25 };
            for (int layer = 0; layer < 3; layer++)
            {
                double yOff = Clamp(-(camY - 300) * par[layer] * 0.5, -40, 140);
                var pts = new List<PointF>();
                for (int sx = -16; sx <= W + 16; sx += 16)
                {
                    double wx = sx + camX * par[layer];
                    double h = (Math.Sin(wx * 0.003 + layer * 2.1) * 0.5 + 0.5) * 160
                             + (Math.Sin(wx * 0.011 + layer) * 0.5 + 0.5) * 50;
                    double baseY = H * (0.58 + 0.1 * layer) + yOff;
                    pts.Add(new PointF(sx, (float)(baseY - h)));
                }
                pts.Add(new PointF(W + 16, H));
                pts.Add(new PointF(-16, H));
                Color far = Lerp(Color.FromArgb(150, 170, 205), Color.FromArgb(70, 80, 130), p);
                Color near = Lerp(Color.FromArgb(95, 120, 150), Color.FromArgb(40, 50, 90), p);
                using (var br = new SolidBrush(Lerp(far, near, layer / 2.0)))
                    g.FillPolygon(br, pts.ToArray());
            }
        }

        void DrawTerrain(Graphics g, int W, int H, double p)
        {
            var edge = new List<PointF>();
            for (int sx = -12; sx <= W + 12; sx += 6)
            {
                double wx = camX + sx;
                edge.Add(new PointF(sx, (float)(Terrain(wx) - camY)));
            }
            var poly = new List<PointF>(edge);
            poly.Add(new PointF(W + 12, H + 20));
            poly.Add(new PointF(-12, H + 20));

            Color fill = Lerp(Color.FromArgb(105, 80, 55), Color.FromArgb(95, 95, 108), p);
            Color grass = p < 0.5
                ? Lerp(Color.FromArgb(70, 155, 60), Color.FromArgb(135, 135, 140), p / 0.5)
                : Lerp(Color.FromArgb(135, 135, 140), Color.FromArgb(245, 248, 255), (p - 0.5) / 0.5);

            using (var br = new SolidBrush(fill)) g.FillPolygon(br, poly.ToArray());
            using (var pen = new Pen(grass, 9) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, edge.ToArray());
        }

        void DrawSigns(Graphics g, int W)
        {
            // progress signs every 10%
            for (int i = 1; i <= 9; i++)
            {
                double x = SummitX * i / 10.0;
                float sx = (float)(x - camX);
                if (sx < -60 || sx > W + 60) continue;
                float sy = (float)(Terrain(x) - camY);
                using (var pen = new Pen(Color.FromArgb(80, 55, 35), 4)) g.DrawLine(pen, sx, sy, sx, sy - 40);
                g.FillRectangle(Brushes.SaddleBrown, sx - 26, sy - 62, 52, 24);
                g.DrawString(i * 10 + "%", smallFont, Brushes.White, sx - 20, sy - 61);
            }

            // summit flag
            float fx = (float)(SummitX - camX);
            if (fx > -100 && fx < W + 100)
            {
                float fy = (float)(Terrain(SummitX) - camY);
                using (var pen = new Pen(Color.Silver, 5)) g.DrawLine(pen, fx, fy, fx, fy - 130);
                g.FillPolygon(Brushes.Red, new[] { new PointF(fx, fy - 130), new PointF(fx + 60, fy - 112), new PointF(fx, fy - 94) });
                g.DrawString("SUMMIT", medFont, Brushes.White, fx - 50, fy - 170);
            }
        }

        void DrawCans(Graphics g, int W)
        {
            for (int i = 0; i < CanCount; i++)
            {
                if (gotCan[i]) continue;
                double px = CanStart + i * CanSpacing;
                float sx = (float)(px - camX);
                if (sx < -40 || sx > W + 40) continue;
                float sy = (float)(Terrain(px) - camY);
                g.FillRectangle(Brushes.Firebrick, sx - 10, sy - 38, 20, 26);
                g.FillRectangle(Brushes.DimGray, sx - 4, sy - 44, 8, 6);
                g.DrawString("F", smallFont, Brushes.White, sx - 7, sy - 38);
            }
        }

        void DrawTruck(Graphics g)
        {
            GraphicsState gs = g.Save();
            g.TranslateTransform((float)(cx - camX), (float)(cy - camY));
            g.RotateTransform((float)(angle * 180 / Math.PI));

            // truck bed
            g.FillPolygon(new SolidBrush(Color.FromArgb(110, 25, 25)),
                new[] { new PointF(-62, -30), new PointF(-62, -48), new PointF(-8, -48), new PointF(-8, -30) });
            // chassis
            g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 65)), -64, -30, 128, 16);
            // cab
            g.FillPolygon(new SolidBrush(Color.FromArgb(200, 40, 40)),
                new[] { new PointF(-8, -30), new PointF(-8, -64), new PointF(18, -64), new PointF(38, -42), new PointF(64, -38), new PointF(64, -30) });
            // window
            g.FillPolygon(new SolidBrush(Color.LightSkyBlue),
                new[] { new PointF(-2, -58), new PointF(15, -58), new PointF(30, -43), new PointF(-2, -43) });
            // headlight + bumper
            g.FillRectangle(Brushes.Gold, 57, -37, 7, 6);
            g.FillRectangle(Brushes.Silver, 62, -28, 5, 8);

            // wheels
            foreach (double wx in new[] { -HalfBase, HalfBase })
            {
                float x = (float)wx;
                g.FillEllipse(Brushes.Black, x - (float)R, -(float)R, (float)(2 * R), (float)(2 * R));
                g.FillEllipse(Brushes.Silver, x - 8, -8, 16, 16);
                using (var pen = new Pen(Color.DimGray, 3))
                {
                    for (int k = 0; k < 2; k++)
                    {
                        double a = wheelRot + k * Math.PI / 2;
                        float dx = (float)(Math.Cos(a) * 8), dy = (float)(Math.Sin(a) * 8);
                        g.DrawLine(pen, x - dx, -dy, x + dx, dy);
                    }
                }
            }
            g.Restore(gs);
        }

        void DrawHud(Graphics g, int W, int H, double p)
        {
            // progress bar
            g.DrawString("SUMMIT  " + (int)(p * 100) + "%", hudFont, Brushes.White, 18, 10);
            g.FillRectangle(new SolidBrush(Color.FromArgb(120, 0, 0, 0)), 20, 40, 300, 14);
            g.FillRectangle(Brushes.DeepSkyBlue, 20, 40, (float)(300 * p), 14);
            g.DrawRectangle(Pens.White, 20, 40, 300, 14);

            // stats
            double elevation = 1000 + (600 - cy) / 2;
            string stats = string.Format("Elevation {0:N0} m     Speed {1:0} km/h     Time {2}",
                elevation, Math.Abs(v) * 0.12, FormatTime(time));
            g.DrawString(stats, smallFont, Brushes.White, 20, 62);

            // fuel
            g.DrawString("FUEL", hudFont, Brushes.White, W - 240, 10);
            g.FillRectangle(new SolidBrush(Color.FromArgb(120, 0, 0, 0)), W - 240, 40, 220, 14);
            bool low = fuel < 25;
            Brush fb = low ? (((int)(time * 4) % 2 == 0) ? Brushes.Red : Brushes.OrangeRed) : Brushes.LimeGreen;
            g.FillRectangle(fb, W - 240, 40, (float)(220 * fuel / MaxFuel), 14);
            g.DrawRectangle(Pens.White, W - 240, 40, 220, 14);

            if (state == State.Playing && time < 6)
                g.DrawString("RIGHT / D = gas     LEFT / A = brake & reverse     In the air: gas / brake tilts the truck",
                    smallFont, Brushes.White, 20, H - 34);
        }

        void DrawOverlay(Graphics g, int W, int H)
        {
            using (var br = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillRectangle(br, 0, 0, W, H);

            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            string title, line1, line2 = "";
            Brush titleBrush = Brushes.White;

            switch (state)
            {
                case State.Title:
                    title = "MOUNTAIN DASH";
                    titleBrush = Brushes.Orange;
                    line1 = "Drive your truck to the summit before the fuel runs out!";
                    line2 = "RIGHT / D = gas    LEFT / A = brake    R = restart\n\nPress ENTER to start";
                    if (best > 0) line2 += "\n\nBest time: " + FormatTime(best);
                    break;
                case State.Crashed:
                    title = "WIPEOUT!";
                    titleBrush = Brushes.OrangeRed;
                    line1 = "You landed on your roof at " + (int)(Clamp(cx / SummitX, 0, 1) * 100) + "% of the climb.";
                    line2 = "Press ENTER or R to try again";
                    break;
                case State.NoFuel:
                    title = "OUT OF FUEL";
                    titleBrush = Brushes.Gold;
                    line1 = "Stranded at " + (int)(Clamp(cx / SummitX, 0, 1) * 100) + "% of the climb. Grab those red fuel cans!";
                    line2 = "Press ENTER or R to try again";
                    break;
                default:
                    title = "SUMMIT REACHED!";
                    titleBrush = Brushes.LimeGreen;
                    line1 = "Time: " + FormatTime(time) + "      Best: " + FormatTime(best);
                    line2 = "Press ENTER or R to beat your time";
                    break;
            }

            g.DrawString(title, bigFont, titleBrush, new RectangleF(0, H * 0.12f, W, 120), sf);
            g.DrawString(line1, medFont, Brushes.White, new RectangleF(0, H * 0.42f, W, 40), sf);
            g.DrawString(line2, medFont, Brushes.White, new RectangleF(0, H * 0.62f, W, 160), sf);
        }

        static string FormatTime(double t)
        {
            int m = (int)(t / 60);
            double s = t - m * 60;
            return string.Format("{0}:{1:00.0}", m, s);
        }
    }


}
