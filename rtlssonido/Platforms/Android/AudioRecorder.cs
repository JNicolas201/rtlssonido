using Android.Media;
using System;

namespace rtlssonido.Platforms.Android;

public class AudioRecorder
{
    const int SAMPLE_RATE = 44100;

    static AudioRecord _recorder = null;
    static int _bufferSize = 0;
    static readonly object _lock = new object();

    void IniciarSiNoEstaActivo()
    {
        lock (_lock)
        {
            if (_recorder == null)
            {
                _bufferSize = AudioRecord.GetMinBufferSize(
                    SAMPLE_RATE,
                    ChannelIn.Mono,
                    Encoding.Pcm16bit);

                if (_bufferSize <= 0)
                    throw new InvalidOperationException($"GetMinBufferSize inválido: {_bufferSize}");

                _recorder = new AudioRecord(
                    AudioSource.Mic,
                    SAMPLE_RATE,
                    ChannelIn.Mono,
                    Encoding.Pcm16bit,
                    _bufferSize * 4);

                if (_recorder.State != State.Initialized)
                {
                    _recorder.Release();
                    _recorder = null;
                    throw new InvalidOperationException("AudioRecord no se inicializó correctamente");
                }

                _recorder.StartRecording();
            }
        }
    }

    public short[] Grabar(int duracionMs)
    {
        IniciarSiNoEstaActivo();

        int total = SAMPLE_RATE * duracionMs / 1000;
        short[] buffer = new short[total];

        int leidos = 0;
        while (leidos < total)
        {
            int chunk = _recorder.Read(buffer, leidos,
                        Math.Min(_bufferSize / 2, total - leidos));
            if (chunk > 0) leidos += chunk;
            else if (chunk == 0) break;
            else throw new InvalidOperationException($"AudioRecord.Read error: {chunk}");
        }

        return buffer;
    }
}