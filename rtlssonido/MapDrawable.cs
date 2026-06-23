using Microsoft.Maui.Graphics;
using System.Numerics;

namespace rtlssonido
{
    // Dibuja un mapa 2D simple en metros
    // - origin: coordenadas en metros de la esquina central/visible
    // - scale: pixeles por metro
    public class MapDrawable : IDrawable
    {
        public float Scale { get; set; } = 50f; // 50 px por metro
        public Vector2 OriginMeters { get; set; } = new Vector2(0, 0); // centro en metros

        // posición actual en metros
        public Vector2? PositionMeters { get; set; }

        // histórico de posiciones (opcional)
        public System.Collections.Generic.List<Vector2> Trail { get; } = new();

        // Balizas fijas en metros (una por esquina, por ejemplo)
        public System.Collections.Generic.List<Vector2> BeaconsMeters { get; } = new();

        public void ClearTrail() => Trail.Clear();

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();

            // Fondo
            canvas.FillColor = Colors.Black;
            canvas.FillRectangle(dirtyRect);

            // Centro del view en píxeles
            float cx = dirtyRect.Width / 2f;
            float cy = dirtyRect.Height / 2f;

            // Draw grid (1m spacing)
            canvas.StrokeColor = Colors.DimGray;
            canvas.StrokeSize = 1;

            int halfW = (int)Math.Ceiling(dirtyRect.Width / (Scale));
            int halfH = (int)Math.Ceiling(dirtyRect.Height / (Scale));

            for (int gx = -halfW; gx <= halfW; gx++)
            {
                float x = cx + (gx - OriginMeters.X) * Scale;
                canvas.DrawLine(x, 0, x, dirtyRect.Height);
            }

            for (int gy = -halfH; gy <= halfH; gy++)
            {
                float y = cy + (gy + OriginMeters.Y) * Scale; // Y positive up in meters -> down in pixels
                canvas.DrawLine(0, y, dirtyRect.Width, y);
            }

            // Ejes
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2;
            // eje X
            canvas.DrawLine(0, cy, dirtyRect.Width, cy);
            // eje Y
            canvas.DrawLine(cx, 0, cx, dirtyRect.Height);

            // Draw trail as fading points (no continuous line)
            if (Trail.Count > 0)
            {
                // draw each historic point as a small filled circle
                for (int i = 0; i < Trail.Count; i++)
                {
                    var p = MetersToPixels(Trail[i], cx, cy);
                    // alpha: older points are more transparent, newest is opaque
                    float alpha = Trail.Count == 1 ? 1f : (float)(i + 1) / (float)Trail.Count;
                    var baseColor = Colors.Orange;
                    var col = new Color(baseColor.Red, baseColor.Green, baseColor.Blue, alpha);
                    canvas.FillColor = col;
                    // radius slightly larger for newer points
                    float radius = 3f + 3f * alpha;
                    canvas.FillCircle(p.X, p.Y, radius);
                }
            }

            // Draw beacons
            if (BeaconsMeters.Count > 0)
            {
                for (int i = 0; i < BeaconsMeters.Count; i++)
                {
                    var bm = BeaconsMeters[i];
                    var bp = MetersToPixels(bm, cx, cy);
                    canvas.FillColor = Colors.Red;
                    canvas.FillCircle(bp.X, bp.Y, 6);
                    canvas.FontColor = Colors.White;
                    canvas.FontSize = 12;
                    canvas.DrawString($"B{i}", bp.X + 8, bp.Y - 8, HorizontalAlignment.Left);
                }
            }

            // Draw current position
            if (PositionMeters.HasValue)
            {
                var p = MetersToPixels(PositionMeters.Value, cx, cy);
                canvas.FillColor = Colors.Lime;
                canvas.FillCircle(p.X, p.Y, 8);

                canvas.FontColor = Colors.White;
                canvas.FontSize = 12;
                canvas.DrawString($"{PositionMeters.Value.X:F2}m, {PositionMeters.Value.Y:F2}m",
                    p.X + 10, p.Y - 10, HorizontalAlignment.Left);
            }

            canvas.RestoreState();
        }

        System.Numerics.Vector2 MetersToPixels(Vector2 m, float cx, float cy)
        {
            float px = cx + (m.X - OriginMeters.X) * Scale;
            // Invert Y: meters positive up -> pixels positive down
            float py = cy - (m.Y - OriginMeters.Y) * Scale;
            return new Vector2(px, py);
        }
    }
}
