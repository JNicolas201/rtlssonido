using Microsoft.Maui.Graphics;

namespace rtlssonido;

public class SpectrumDrawable : IDrawable
{
    public float[] Magnitudes { get; set; } = new float[0];
    public int SampleRate { get; set; } = 44100;
    public (int f0, int f1)[] BeaconBands { get; set; } = new (int, int)[0];

    public void Draw(ICanvas canvas, RectF rect)
    {
        canvas.FillColor = Colors.Black;
        canvas.FillRectangle(rect);

        if (Magnitudes.Length == 0) return;

        // Mostrar rango 10000 - 22000 Hz
        int minHz = 10000;
        int maxHz = 22000;
        float binWidth = (float)SampleRate / 2 / Magnitudes.Length;

        int startBin = (int)(minHz / binWidth);
        int endBin = Math.Min((int)(maxHz / binWidth), Magnitudes.Length);

        // Encontrar máximo para escalar
        float max = 0.001f;
        for (int i = startBin; i < endBin; i++)
            if (Magnitudes[i] > max) max = Magnitudes[i];

        // Dibujar bandas de balizas como rectángulos de color
        Color[] colors = { Colors.Red, Colors.Orange, Colors.Yellow, Colors.Cyan };
        for (int b = 0; b < BeaconBands.Length; b++)
        {
            float xStart = (float)(BeaconBands[b].f0 - minHz) / (maxHz - minHz) * rect.Width;
            float xEnd = (float)(BeaconBands[b].f1 - minHz) / (maxHz - minHz) * rect.Width;
            canvas.FillColor = colors[b % 4].WithAlpha(0.2f);
            canvas.FillRectangle(xStart, 0, xEnd - xStart, rect.Height);

            // Etiqueta
            canvas.FontColor = colors[b % 4];
            canvas.FontSize = 12;
            canvas.DrawString($"B{b}", xStart + 2, 14, HorizontalAlignment.Left);
        }

        // Dibujar el espectro
        canvas.StrokeColor = Colors.LimeGreen;
        canvas.StrokeSize = 1.5f;
        PathF path = new PathF();
        bool first = true;
        for (int i = startBin; i < endBin; i++)
        {
            float freq = i * binWidth;
            float x = (freq - minHz) / (maxHz - minHz) * rect.Width;
            float y = rect.Height - (Magnitudes[i] / max) * rect.Height;
            if (first) { path.MoveTo(x, y); first = false; }
            else path.LineTo(x, y);
        }
        canvas.DrawPath(path);

        // Eje de frecuencias
        canvas.FontColor = Colors.White;
        canvas.FontSize = 10;
        for (int hz = minHz; hz <= maxHz; hz += 2000)
        {
            float x = (float)(hz - minHz) / (maxHz - minHz) * rect.Width;
            canvas.DrawLine(x, rect.Height - 5, x, rect.Height);
            canvas.DrawString($"{hz / 1000}k", x + 2, rect.Height - 6, HorizontalAlignment.Left);
        }
    }
}
