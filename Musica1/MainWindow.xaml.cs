using System;
using System.Linq;
using System.Numerics;
using System.Windows;
using NAudio.Wave;
using MathNet.Numerics.IntegralTransforms;
using System.Windows.Controls;

namespace AudioCaptureWPF
{
    public partial class MainWindow : Window
    {
        private IWaveIn waveIn;
        private WaveFormat waveFormat;
        private int bufferSize = 8192; // Tamaño del búfer en bytes
        private byte[] buffer;
        private bool isCapturing = false;
        private float[] lowPassFilterCoefficients;
        private float[] delayLine;
        private int delayLineIndex;

        public MainWindow()
        {
            InitializeComponent();
            LoadAudioDevices();
            InitializeLowPassFilter();
        }

        private void LoadAudioDevices()
        {
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var deviceInfo = WaveIn.GetCapabilities(i);
                devicesListBox.Items.Add(deviceInfo.ProductName);
            }

            if (devicesListBox.Items.Count > 0)
            {
                devicesListBox.SelectedIndex = 0;
            }
        }

        private void InitializeLowPassFilter()
        {
            // Inicializar un filtro de paso bajo con una frecuencia de corte de 1000 Hz
            int sampleRate = 44100; // Frecuencia de muestreo
            double cutoffFrequency = 1000; // Frecuencia de corte
            int filterOrder = 64; // Orden del filtro

            lowPassFilterCoefficients = CreateLowPassFilterCoefficients(sampleRate, cutoffFrequency, filterOrder);
            delayLine = new float[filterOrder];
        }

        private float[] CreateLowPassFilterCoefficients(int sampleRate, double cutoffFrequency, int filterOrder)
        {
            float[] coefficients = new float[filterOrder];
            double fc = cutoffFrequency / sampleRate;
            for (int i = 0; i < filterOrder; i++)
            {
                if (i == (filterOrder - 1) / 2)
                {
                    coefficients[i] = (float)(2 * fc);
                }
                else
                {
                    coefficients[i] = (float)(Math.Sin(2 * Math.PI * fc * (i - (filterOrder - 1) / 2)) / (Math.PI * (i - (filterOrder - 1) / 2)));
                }

                // Apply a Hamming window
                coefficients[i] *= (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (filterOrder - 1)));
            }
            return coefficients;
        }

        private void DevicesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Detenemos la captura si se está realizando
            StopCapturing();

            // Comenzamos la captura con el nuevo dispositivo seleccionado
            StartCapturing();
        }

        private void StartCapturing()
        {
            if (devicesListBox.SelectedIndex < 0)
            {
                MessageBox.Show("Please select an audio device.");
                return;
            }

            waveFormat = new WaveFormat(44100, 1); // Formato de audio: 44100 Hz, 16-bit, Mono
            waveIn = new WaveInEvent
            {
                DeviceNumber = devicesListBox.SelectedIndex,
                WaveFormat = waveFormat
            };

            // Configurar el evento DataAvailable para procesar los datos de audio capturados
            waveIn.DataAvailable += WaveIn_DataAvailable;

            // Inicializar el búfer
            buffer = new byte[bufferSize];

            // Iniciar la captura de audio
            waveIn.StartRecording();
            isCapturing = true;
        }

        private void StopCapturing()
        {
            if (waveIn != null)
            {
                waveIn.StopRecording();
                waveIn.Dispose();
                isCapturing = false;
            }
        }

        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            // Procesar los datos de audio capturados en tiempo real
            if (isCapturing)
            {
                // Convertir los bytes capturados en datos de audio
                float[] audioData = new float[e.BytesRecorded / 2];
                for (int i = 0; i < e.BytesRecorded / 2; i++)
                {
                    audioData[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                }

                // Aplicar el filtro de paso bajo
                float[] filteredAudioData = ApplyLowPassFilter(audioData);

                // Realizar el análisis de audio en tiempo real y calcular la nota
                string note = AnalyzeAudioData(filteredAudioData);

                // Actualizar la interfaz de usuario con la nota detectada
                Dispatcher.Invoke(() => currentNoteTextBlock.Text = $"Current Note: {note}");
            }
        }

        private float[] ApplyLowPassFilter(float[] input)
        {
            float[] output = new float[input.Length];

            for (int n = 0; n < input.Length; n++)
            {
                delayLine[delayLineIndex] = input[n];
                float yn = 0;
                int index = delayLineIndex;

                for (int i = 0; i < lowPassFilterCoefficients.Length; i++)
                {
                    yn += lowPassFilterCoefficients[i] * delayLine[index];
                    index = (index > 0) ? index - 1 : lowPassFilterCoefficients.Length - 1;
                }

                output[n] = yn;
                delayLineIndex = (delayLineIndex + 1) % lowPassFilterCoefficients.Length;
            }

            return output;
        }

        private string AnalyzeAudioData(float[] audioData)
        {
            // Aplicar la FFT
            Complex[] fftResult = audioData.Select(d => new Complex(d, 0)).ToArray();
            Fourier.Forward(fftResult, FourierOptions.Matlab);

            // Obtener la magnitud de las frecuencias
            double[] magnitudes = fftResult.Select(c => c.Magnitude).ToArray();

            // Encontrar la frecuencia dominante
            int maxIndex = Array.IndexOf(magnitudes, magnitudes.Max());
            double dominantFrequency = maxIndex * waveFormat.SampleRate / (double)fftResult.Length;

            // Convertir la frecuencia en una nota
            return FrequencyToNote(dominantFrequency);
        }

        private string FrequencyToNote(double frequency)
        {
            // Tabla de frecuencias para las notas musicales
            string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            double a4 = 440.0; // Frecuencia de la nota A4
            double c0 = a4 * Math.Pow(2, -4.75); // Frecuencia de la nota C0
            int halfSteps = (int)Math.Round(12 * Math.Log2(frequency / c0));
            int noteIndex = halfSteps % 12;
            int octave = halfSteps / 12;

            return noteNames[(noteIndex + 12) % 12] + octave;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopCapturing();
        }
    }
}
