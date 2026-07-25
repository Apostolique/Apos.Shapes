using System;
using System.IO;

// Builds the elliptical arc length table that Apos.Shapes embeds. See EllipseArc.cs for what the
// shader does with it, and EllipseArcGen.csproj for how to run this.
//
// Rows are the aspect ratio b/a, columns run along one quadrant, which symmetry extends to the
// whole ellipse. Two maps are needed and they want different parameterizations, so each texel
// packs both as 16 bit fractions, high byte first: the inverse, theta at a given arc length, in
// RG, and the forward, arc length at a given theta, in BA.
//
// Both the column and the row axis are sqrt warped, and that is load bearing rather than polish.
// Past a tip region only b/a radians wide, arc length along a thin ellipse grows like a*theta^2/2,
// so theta(s) ~ sqrt(2s/a) has unbounded slope at the tip: sampled evenly in s a table converges
// first order and drifts by a quarter pixel at 100:1, while against w = sqrt(s/S) the same
// function is near linear and the table is eccentricity independent. The row axis has the same
// shape, since the aspect ratio enters through that tip region.
static class Program {
    const int Width = 256;
    const int Height = 64;
    const double HalfPi = Math.PI / 2d;

    // Panels the quadrant is cut into for the cumulative arc. Eight point Gauss-Legendre on each
    // is exact to a few parts in 10^11 even on the thinnest row, five orders below the 16 bit
    // storage step, so the table's only real error is the quantization.
    const int Panels = 256;
    const double PanelWidth = HalfPi / Panels;

    static readonly double[] GaussX = {
        -0.9602898564975363, -0.7966664774136267, -0.5255324099163290, -0.1834346424956498,
         0.1834346424956498,  0.5255324099163290,  0.7966664774136267,  0.9602898564975363 };
    static readonly double[] GaussW = {
        0.1012285362903763, 0.2223810344533745, 0.3137066458778873, 0.3626837833783620,
        0.3626837833783620, 0.3137066458778873, 0.2223810344533745, 0.1012285362903763 };

    // Arc length over one interval of the unit major axis ellipse with minor axis rho, measuring
    // theta from the major axis tip: the integrand is sqrt(sin^2 + rho^2 cos^2).
    static double Panel(double lo, double hi, double rho) {
        double h = 0.5d * (hi - lo);
        double mid = 0.5d * (lo + hi);
        double acc = 0d;
        for (int k = 0; k < GaussX.Length; k++) {
            double t = mid + h * GaussX[k];
            double s = Math.Sin(t);
            double c = Math.Cos(t);
            acc += GaussW[k] * Math.Sqrt(s * s + rho * rho * c * c);
        }
        return acc * h;
    }
    static void BuildCumulative(double[] cum, double rho) {
        cum[0] = 0d;
        for (int i = 0; i < Panels; i++) {
            cum[i + 1] = cum[i] + Panel(i * PanelWidth, (i + 1) * PanelWidth, rho);
        }
    }
    static double Arc(double[] cum, double rho, double theta) {
        theta = Math.Clamp(theta, 0d, HalfPi);
        int i = Math.Min((int)(theta / PanelWidth), Panels - 1);
        return cum[i] + Panel(i * PanelWidth, theta, rho);
    }
    // Theta at a given arc length: bracket in the cumulative table, then Newton on the exact arc,
    // whose derivative is the speed. Starting inside one panel it converges in a couple of steps.
    static double Inverse(double[] cum, double rho, double target) {
        int lo = 0;
        int hi = Panels;
        while (hi - lo > 1) {
            int mid = (lo + hi) / 2;
            if (cum[mid] <= target) lo = mid; else hi = mid;
        }
        double theta = lo * PanelWidth;
        for (int k = 0; k < 8; k++) {
            double s = Math.Sin(theta);
            double c = Math.Cos(theta);
            double speed = Math.Sqrt(s * s + rho * rho * c * c);
            theta = Math.Clamp(theta + (target - Arc(cum, rho, theta)) / Math.Max(speed, 1e-12d), 0d, HalfPi);
        }
        return theta;
    }

    static void Main(string[] args) {
        var data = new byte[Width * Height * 4];
        var cum = new double[Panels + 1];
        for (int j = 0; j < Height; j++) {
            double t = j / (double)(Height - 1);
            double rho = t * t;
            BuildCumulative(cum, rho);
            double quarter = cum[Panels];

            for (int i = 0; i < Width; i++) {
                double col = i / (double)(Width - 1);
                double forward = Arc(cum, rho, col * HalfPi) / quarter;
                double inverse = Inverse(cum, rho, quarter * col * col) / HalfPi;
                int x = Quantize(inverse);
                int y = Quantize(forward);
                int o = (j * Width + i) * 4;
                data[o + 0] = (byte)(x >> 8);
                data[o + 1] = (byte)(x & 0xFF);
                data[o + 2] = (byte)(y >> 8);
                data[o + 3] = (byte)(y & 0xFF);
            }
        }

        string path = args.Length > 0 ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "../../../../../Source/Content/ellipse-arc.lut");
        path = Path.GetFullPath(path);
        File.WriteAllBytes(path, data);
        Console.WriteLine($"wrote {data.Length} bytes to {path}");
    }

    static int Quantize(double v) => (int)Math.Clamp(Math.Round(v * 65535d), 0d, 65535d);
}
