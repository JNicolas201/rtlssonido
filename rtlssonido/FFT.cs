using System;
using System.Numerics;

namespace rtlssonido;

public static class FFT
{
    // FFT iterativa Cooley-Tukey
    public static Complex[] Transform(float[] samples)
    {
        int n = samples.Length;
        // n debe ser potencia de 2
        int nPow2 = 1;
        while (nPow2 < n) nPow2 *= 2;

        Complex[] data = new Complex[nPow2];
        for (int i = 0; i < n; i++) data[i] = new Complex(samples[i], 0);
        for (int i = n; i < nPow2; i++) data[i] = Complex.Zero;

        // Bit reversal
        int j = 0;
        for (int i = 1; i < nPow2; i++)
        {
            int bit = nPow2 >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }

        // Butterfly
        for (int len = 2; len <= nPow2; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            Complex wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (int i = 0; i < nPow2; i += len)
            {
                Complex w = Complex.One;
                for (int k = 0; k < len / 2; k++)
                {
                    Complex u = data[i + k];
                    Complex v = data[i + k + len / 2] * w;
                    data[i + k] = u + v;
                    data[i + k + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }

        return data;
    }

    // Devuelve magnitudes (solo primera mitad útil)
    public static float[] Magnitudes(float[] samples)
    {
        Complex[] fft = Transform(samples);
        int half = fft.Length / 2;
        float[] mag = new float[half];
        for (int i = 0; i < half; i++)
            mag[i] = (float)fft[i].Magnitude;
        return mag;
    }
}
