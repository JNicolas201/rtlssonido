using Microsoft.Maui.Controls.PlatformConfiguration;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Microsoft.Maui.Graphics; // para `Colors` y `Color`
using Microsoft.Maui.ApplicationModel; // para `MainThread`
using System.Linq; // para `Select`
using System.Numerics;
using Microsoft.Maui.Devices.Sensors;

namespace rtlssonido;   // ← namespace correcto

public partial class MainPage : ContentPage
{
    // ⚠️ Cambia esta IP: abre CMD y escribe ipconfig → copia IPv4
    const string SERVIDOR_URL = "http://192.168.6.201:5000/timestamps";

    const int SAMPLE_RATE = 44100;
    const int CHIRP_MS = 30;

    readonly int[] FREQ_BALIZAS = new int[] {
    15500,
    16500,
    17500,
    18500,
};

    bool _corriendo = false;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

    MapDrawable _mapDrawable;
    SpectrumDrawable _spectrumDrawable;
    // Sensores
    bool _sensorsRunning = false;
    Vector3 _lastAccel = Vector3.Zero;
    Vector3 _lastGyro = Vector3.Zero;
    Vector3 _velocity = Vector3.Zero; // m/s
    Vector3 _posFromAccel = Vector3.Zero; // m
    DateTime _lastAccelTime = DateTime.MinValue;
    // posición ancla recibida del servidor
    Vector2? _lastServerPos = null;
    DateTime _lastServerTime = DateTime.MinValue;
    // UI threshold factor
    float _thresholdFactor = 0.35f;

    public MainPage()
    {
        InitializeComponent();

        // Inicializa el drawable del mapa y asigna al GraphicsView
        _mapDrawable = new MapDrawable();
        mapView.Drawable = _mapDrawable;

        // Spectrum drawable
        _spectrumDrawable = new SpectrumDrawable();
        _spectrumDrawable.BeaconBands = FREQ_BALIZAS.Select(f => (f - 250, f + 250)).ToArray();
        spectrumView.Drawable = _spectrumDrawable;

        // Default: area de 2x2 m, balizas a 2m de separación (esquinas)
        SetBeaconsByArea(2f);

        // Optionally set origin and scale to adjust view
        _mapDrawable.OriginMeters = new Vector2(0, 0); // centro en (0,0)
        _mapDrawable.Scale = 60f; // pixeles por metro

        // Force initial draw
        mapView.Invalidate();

        // Set entry default
        entryAreaSize.Text = "2";

        // Inicializa slider y label
        sliderThreshold.Value = _thresholdFactor;
        lblThresholdVal.Text = _thresholdFactor.ToString("F2");
    }


    //prueba de commit

    void OnThresholdChanged(object sender, ValueChangedEventArgs e)
    {
        // Snap to 0.05 steps
        double v = Math.Round(e.NewValue / 0.05) * 0.05;
        _thresholdFactor = (float)v;
        sliderThreshold.Value = v;
        lblThresholdVal.Text = _thresholdFactor.ToString("F2");
    }

    void OnResetThresholdClicked(object sender, EventArgs e)
    {
        _thresholdFactor = 0.35f;
        sliderThreshold.Value = _thresholdFactor;
        lblThresholdVal.Text = _thresholdFactor.ToString("F2");
    }

    void StartSensors()
    {
        // SENSORES DESACTIVADOS: por ahora no arrancamos acelerómetro/giroscopio
        System.Diagnostics.Debug.WriteLine("StartSensors: sensores desactivados por configuración.");
        _sensorsRunning = false;
    }

    void StopSensors()
    {
        // StopSensors: no hay sensores activos (desactivados)
        System.Diagnostics.Debug.WriteLine("StopSensors: sensores desactivados por configuración.");
        _sensorsRunning = false;
        _lastAccelTime = DateTime.MinValue;
        _velocity = Vector3.Zero;
        _posFromAccel = Vector3.Zero;
    }

    void OnAccelerometerChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        // e.Reading.Acceleration has X,Y,Z as double
        var a = e.Reading.Acceleration;
        var accel = new Vector3((float)a.X, (float)a.Y, (float)a.Z);
        var now = DateTime.UtcNow;
        if (_lastAccelTime != DateTime.MinValue)
        {
            float dt = (float)(now - _lastAccelTime).TotalSeconds;
            if (dt > 0 && dt < 1)
            {
                // Simple integration: v += a*dt ; p += v*dt
                _velocity += accel * dt;
                // apply damping to reduce drift
                _velocity *= 0.85f;
                _posFromAccel += _velocity * dt;
            }
        }
        _lastAccelTime = now;
        _lastAccel = accel;

        // update UI and predicted position using last server anchor + integrated displacement
        MainThread.BeginInvokeOnMainThread(() =>
        {
            lblAccel.Text = $"X:{accel.X:F2} Y:{accel.Y:F2} Z:{accel.Z:F2}";
            lblSensorDisp.Text = $"Disp: X:{_posFromAccel.X:F2} Y:{_posFromAccel.Y:F2} m";

            Vector2 displayPos;
            if (_lastServerPos.HasValue)
            {
                displayPos = _lastServerPos.Value + new Vector2(_posFromAccel.X, _posFromAccel.Y);
            }
            else
            {
                displayPos = new Vector2(_posFromAccel.X, _posFromAccel.Y);
            }

            _mapDrawable.PositionMeters = displayPos;
            // don't add sensor-only points to trail to avoid polluting server trail
            mapView.Invalidate();
        });
    }

    void OnGyroscopeChanged(object? sender, GyroscopeChangedEventArgs e)
    {
        var g = e.Reading.AngularVelocity;
        var gyro = new Vector3((float)g.X, (float)g.Y, (float)g.Z);
        _lastGyro = gyro;
        MainThread.BeginInvokeOnMainThread(() => lblGyro.Text = $"X:{gyro.X:F2} Y:{gyro.Y:F2} Z:{gyro.Z:F2}");
    }

    async void OnIniciarClicked(object sender, EventArgs e)
    {
        _corriendo = !_corriendo;
        if (_corriendo)
        {
            btnIniciar.Text = "Detener";
            btnIniciar.BackgroundColor = Colors.Red;
            lblEstado.Text = "Grabando...";
            StartSensors();
            await Task.Run(LoopDeteccion);
        }
        else
        {
            btnIniciar.Text = "Iniciar Detección";
            btnIniciar.BackgroundColor = Color.FromArgb("#3182ce");
            lblEstado.Text = "Detenido";
            StopSensors();
        }
    }

    async Task LoopDeteccion()
    {

        while (_corriendo)
        {
            try
            {
                short[] audioRaw = await GrabarAudio(700);
                float[] audio = audioRaw.Select(s => s / 32768f).ToArray();

                if (audio.Length == 0)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                        lblEstado.Text = "No se capturó audio (solo disponible en Android).");
                    await Task.Delay(1000);
                    continue;
                }


                double[] timestamps = DetectarChirps(audio, SAMPLE_RATE);

                if (timestamps.Length == 4)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        lblT0.Text = $"Baliza 0: {timestamps[0]:F4}s";
                        lblT1.Text = $"Baliza 1: {timestamps[1]:F4}s";
                        lblT2.Text = $"Baliza 2: {timestamps[2]:F4}s";
                        lblT3.Text = $"Baliza 3: {timestamps[3]:F4}s";
                        lblEstado.Text = "Evaluando calidad...";
                    });

                    // Paso 1/2 — Ignorar timestamps <= 0 y requerir al menos 3 válidos antes de enviar
                    int validos = timestamps.Count(t => t > 0);
                    if (validos < 3)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            lblEstado.Text = $"Detecciones válidas: {validos}/4 — no se envía";
                        });
                        System.Diagnostics.Debug.WriteLine($"No enviar: solo {validos} timestamps válidos");
                        await Task.Delay(200); // pequeño retardo
                        continue;
                    }

                    // Enviar manteniendo la posición de las balizas: el servidor espera el array de 4
                    // (usar 0 para faltantes). Enviar el array original `timestamps` preserva el orden
                    // y evita que el servidor asigne mal los valores.
                    // Log de timestamps compensados (restar offsets TDMA esperados)
                    double[] tComp = new double[4];
                    tComp[0] = timestamps[0] - 0.000;
                    tComp[1] = timestamps[1] - 0.060;
                    tComp[2] = timestamps[2] - 0.120;
                    tComp[3] = timestamps[3] - 0.180;

                    double rangoMS = (tComp.Max() - tComp.Min()) * 1000;
                    System.Diagnostics.Debug.WriteLine($"COMPENSADOS: {string.Join(", ", tComp.Select(v => v.ToString("F6")))} rango={rangoMS:F1}ms");

                    // Mostrar en la app
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        lblCorrLog.Text += $"\nCompensados (rango={rangoMS:F1}ms):";
                        lblCorrLog.Text += $"\nB0={tComp[0]:F4} B1={tComp[1]:F4}";
                        lblCorrLog.Text += $"\nB2={tComp[2]:F4} B3={tComp[3]:F4}";
                    });

                    System.Diagnostics.Debug.WriteLine($"Enviando timestamps (preservando 4 entradas): {string.Join(',', timestamps)}");
                    MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"Enviando timestamps...");
                    await EnviarAlServidor(timestamps);
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                        lblEstado.Text = $"Detectados: {timestamps.Length}/4 chirps");
                }
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    lblEstado.Text = $"Error: {ex.Message}");
            }
        }
    }

    float[] GenerarTono(int freq, int durMs, int fs)
    {
        int n = fs * durMs / 1000;
        float[] tono = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / fs;
            tono[i] = MathF.Sin(2 * MathF.PI * freq * t);
        }
        return tono;
    }

    double[] DetectarChirps(float[] audio, int fs)
    {
        var timestamps = new double[4];
        bool[] detectado = new bool[4];
        var sb = new StringBuilder();

        // Pre-calcular las 4 correlaciones
        float[][] correlaciones = new float[4][];
        for (int b = 0; b < 4; b++)
        {
            float[] refChirp = GenerarTono(FREQ_BALIZAS[b], CHIRP_MS, fs);

            int n = audio.Length - refChirp.Length;
            if (n <= 0) return Array.Empty<double>();
            float[] corr = new float[n];

            float energiaRef = 0;
            for (int j = 0; j < refChirp.Length; j++)
                energiaRef += refChirp[j] * refChirp[j];

            for (int i = 0; i < n; i++)
            {
                float suma = 0;
                float energia = 0;
                for (int j = 0; j < refChirp.Length; j++)
                {
                    suma += audio[i + j] * refChirp[j];
                    energia += audio[i + j] * audio[i + j];
                }
                corr[i] = MathF.Abs(suma) / MathF.Sqrt(energia * energiaRef + 1e-6f);
            }
            correlaciones[b] = corr;
        }

        // ===== PASO 1: Buscar B0 en TODO el buffer =====
        int nB0 = correlaciones[0].Length;
        float mejorValB0 = 0;
        int mejorB0 = -1;
        for (int i = 0; i < nB0; i++)
        {
            if (correlaciones[0][i] > mejorValB0)
            {
                mejorValB0 = correlaciones[0][i];
                mejorB0 = i;
            }
        }

        const float UMBRAL_ABSOLUTO = 0.4f;

        if (mejorB0 < 0 || mejorValB0 < UMBRAL_ABSOLUTO)
        {
            sb.AppendLine($"B0 no detectada (val={mejorValB0:F3})");
            MainThread.BeginInvokeOnMainThread(() => lblCorrLog.Text = sb.ToString());
            return Array.Empty<double>();
        }

        timestamps[0] = (double)mejorB0 / fs;
        detectado[0] = true;
        sb.AppendLine($"B0: idx={mejorB0} t={timestamps[0]:F4}s val={mejorValB0:F3} (búsqueda completa)");

        // ===== PASO 2: Buscar B1, B2, B3 relativos a B0 =====
        double[] offsetEsperado = { 0.0, 0.060, 0.120, 0.180 }; 
        double margen = 0.007;  // ±10ms

        for (int b = 1; b < 4; b++)
        {
            float[] corr = correlaciones[b];
            double tCentro = timestamps[0] + offsetEsperado[b];
            int inicio = Math.Max(0, (int)((tCentro - margen) * fs));
            int fin = Math.Min((int)((tCentro + margen) * fs), corr.Length);

            // Buscar el MÁXIMO en la ventana (no primer pico)
            float mejorVal = 0;
            int mejor = -1;
            float maxEnVentana = 0;

            for (int i = inicio; i < fin; i++)
            {
                if (corr[i] > mejorVal)
                {
                    mejorVal = corr[i];
                    mejor = i;
                }
            }
            maxEnVentana = mejorVal;

            double tDetectado = mejor >= 0 ? (double)mejor / fs : 0;
            int idxEsperado = (int)(tCentro * fs);
            int delta = mejor - idxEsperado;
            sb.AppendLine($"B{b}: idx={mejor} (Δ={delta}) t={tDetectado:F4}s val={mejorVal:F3} (ventana {(tCentro - margen):F3}-{(tCentro + margen):F3}s)");

            if (mejor >= 0 && mejorVal > UMBRAL_ABSOLUTO)
            {
                timestamps[b] = tDetectado;
                detectado[b] = true;
            }
        }

        int found = detectado.Count(d => d);
        if (found == 4)
        {
            sb.AppendLine($"Aceptados: {found}/4");
            MainThread.BeginInvokeOnMainThread(() => lblCorrLog.Text = sb.ToString());
            return timestamps;
        }
        else
        {
            sb.AppendLine($"Detectados: {found}/4 (rechazado)");
            MainThread.BeginInvokeOnMainThread(() => lblCorrLog.Text = sb.ToString());
            return Array.Empty<double>();
        }
    }

    async Task<short[]> GrabarAudio(int duracionMs)
    {
        return await Task.Run(() =>
        {
 #if ANDROID
        var recorder = new rtlssonido.Platforms.Android.AudioRecorder();
        return recorder.Grabar(duracionMs);
 #else
            return Array.Empty<short>();
 #endif
        });
    }

    async Task EnviarAlServidor(double[] timestamps)
    {
        int maxAttempts = 3;
        int attempt = 0;
        bool sent = false;
        Exception lastEx = null;
        while (attempt < maxAttempts && !sent)
        {
            attempt++;
            try
            {
                System.Diagnostics.Debug.WriteLine($"Intento {attempt} enviar timestamps");
                MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"Enviando al servidor... (intento {attempt})");

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(6));
                var resp = await _http.PostAsJsonAsync(SERVIDOR_URL, new { t = timestamps }, cts.Token);

                var body = await resp.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Servidor response body: {body}");
                MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"Servidor: {body}");

                if (resp.IsSuccessStatusCode)
                {
                    // parse response
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("x", out var px) && root.TryGetProperty("y", out var py))
                        {
                            // Si el servidor devuelve null, descartó la medición
                            if (px.ValueKind == JsonValueKind.Null || py.ValueKind == JsonValueKind.Null)
                            {
                                MainThread.BeginInvokeOnMainThread(() =>
                                    lblEstado.Text = "Servidor descartó medición");
                                sent = true;
                                break;
                            }

                            double x = px.GetDouble();
                            double y = py.GetDouble();

                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                lblPosicion.Text = $"X: {x:F2}m | Y: {y:F2}m";
                                lblEstado.Text = $"Posición recibida: X={x:F2} Y={y:F2}";

                                var pos = new Vector2((float)x, (float)y);
                                _mapDrawable.PositionMeters = pos;
                                _mapDrawable.Trail.Add(pos);
                                if (_mapDrawable.Trail.Count > 200) _mapDrawable.Trail.RemoveAt(0);

                                _lastServerPos = pos;
                                _posFromAccel = Vector3.Zero;
                                _velocity = Vector3.Zero;                         
                                _lastServerTime = DateTime.UtcNow;

                                mapView.Invalidate();
                            });
                            sent = true;
                            break;
                        }
                        else
                        {
                            MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"Respuesta inválida: {body}");
                            // don't retry on invalid response
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Parse error: {ex}");
                        MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"Parse error: {ex.Message}");
                        break;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"HTTP error {resp.StatusCode} body={body}");
                    MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"HTTP error: {resp.StatusCode}");
                    // Retry on server error (5xx), otherwise break
                    if ((int)resp.StatusCode >= 500)
                    {
                        await Task.Delay(500);
                        continue;
                    }
                    break;
                }
            }
            catch (OperationCanceledException oce)
            {
                System.Diagnostics.Debug.WriteLine($"EnviarAlServidor timeout: {oce}");
                lastEx = oce;
                MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"Timeout al enviar (intento {attempt})");
                if (attempt < maxAttempts) await Task.Delay(500);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnviarAlServidor exception: {ex}");
                lastEx = ex;
                MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = $"Error red: {ex.GetBaseException().Message}");
                if (attempt < maxAttempts) await Task.Delay(500);
            }
        }
        if (!sent)
        {
            MainThread.BeginInvokeOnMainThread(() => lblEstado.Text = "No se pudo enviar al servidor");
            // Mostrar alerta con la excepción final para que puedas leerla en el móvil
                if (lastEx != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _ = this.DisplayAlertAsync("Error enviando", lastEx.ToString(), "OK");
                });
            }
        }
    }

    void OnActualizarBalizasClicked(object sender, EventArgs e)
    {
        if (float.TryParse(entryAreaSize.Text, out float area))
        {
            SetBeaconsByArea(area);
            mapView.Invalidate();
        }
        else
        {
            _ = DisplayAlertAsync("Valor inválido", "Introduce un número válido en 'Área (m)'", "OK");
        }
    }

    void SetBeaconsByArea(float areaSize)
    {
        _mapDrawable.BeaconsMeters.Clear();
        // Disposición correcta según servidor:
        // B3 (0,2)──────B2 (2,2)
        // B0 (0,0)──────B1 (2,0)
        float half = areaSize / 2f;
        _mapDrawable.BeaconsMeters.Add(new Vector2(-half, -half)); // B0 inferior-izq
        _mapDrawable.BeaconsMeters.Add(new Vector2(half, -half));  // B1 inferior-der
        _mapDrawable.BeaconsMeters.Add(new Vector2(half, half));   // B2 superior-der  ✅
        _mapDrawable.BeaconsMeters.Add(new Vector2(-half, half));  // B3 superior-izq  ✅
    }

    record PosicionDto(double x, double y);
    void OnZoomInClicked(object sender, EventArgs e)
    {
        _mapDrawable.Scale *= 1.2f;
        mapView.Invalidate();
    }

    void OnZoomOutClicked(object sender, EventArgs e)
    {
        _mapDrawable.Scale /= 1.2f;
        if (_mapDrawable.Scale < 5f) _mapDrawable.Scale = 5f; // limite minimo
        mapView.Invalidate();
    }

    void OnAplicarCoordenadasClicked(object sender, EventArgs e)
    {
        // Try parse each entry, if empty skip
        var coords = new System.Collections.Generic.List<Vector2>();
        bool ok = true;

        Vector2 Parse(string xs, string ys)
        {
            float x = 0, y = 0;
            if (!string.IsNullOrWhiteSpace(xs) && !float.TryParse(xs, out x)) ok = false;
            if (!string.IsNullOrWhiteSpace(ys) && !float.TryParse(ys, out y)) ok = false;
            return new Vector2(x, y);
        }

        coords.Add(Parse(entryB0X.Text, entryB0Y.Text));
        coords.Add(Parse(entryB1X.Text, entryB1Y.Text));
        coords.Add(Parse(entryB2X.Text, entryB2Y.Text));
        coords.Add(Parse(entryB3X.Text, entryB3Y.Text));

        if (!ok)
        {
            _ = DisplayAlertAsync("Error", "Introduce valores numéricos válidos", "OK");
            return;
        }

        // apply only if at least one entry was filled (non-zero)
        if (coords.Any(c => c != default))
        {
            _mapDrawable.BeaconsMeters.Clear();
            foreach (var c in coords) _mapDrawable.BeaconsMeters.Add(c);
            mapView.Invalidate();
        }
    }

    async void OnEspectroClicked(object sender, EventArgs e)
    {
        btnEspectro.IsEnabled = false;
        btnEspectro.Text = "Grabando...";

        try
        {
            // Grabar 500ms
            short[] audioRaw = await GrabarAudio(350);
            float[] audio = audioRaw.Select(s => s / 32768f).ToArray();

            if (audio.Length == 0)
            {
                await DisplayAlertAsync("Error", "No se capturó audio", "OK");
                return;
            }

            // Calcular FFT
            float[] magnitudes = FFT.Magnitudes(audio);

            // Actualizar UI
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _spectrumDrawable.Magnitudes = magnitudes;
                _spectrumDrawable.SampleRate = SAMPLE_RATE;
                spectrumView.Invalidate();
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            btnEspectro.IsEnabled = true;
            btnEspectro.Text = "Ver Espectro";
        }
    }
}