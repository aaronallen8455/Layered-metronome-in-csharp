using System;
using System.Linq;

namespace Pronome
{
    /**<summary>Contains the timing information for each individual beat cell.</summary>*/
    public class BeatCell
    {
        /**<summary>The time length in number of bytes.</summary>*/
        public double ByteInterval;
        /**<summary>The time length in quarter notes</summary>*/
        public double Bpm; // value expressed in BPM time.
        /**<summary>The name of the audio source. Could be a wav file or a pitch.</summary>*/
        public string SourceName;
        /**<summary>The layer that this cell belongs to.</summary>*/
        public Layer Layer;
        /**<summary>The audio source used for this cell.</summary>*/
        public IStreamProvider AudioSource;
        /**<summary>True for HiHat pedal down sounds.</summary>*/
        public bool IsHiHatClosed = false;
        /**<summary>True for HiHat open sounds.</summary>*/
        public bool IsHiHatOpen = false;
        /**<summary>If using hihat sounds, how long in BPM the open hihat sound should last.</summary>*/
        public double hhDuration = 0;

        /**<summary>Sets the 'byteinterval' based on current tempo and audiosource sample rate.</summary>*/
        public void SetBeatValue()
        {
            // set byte interval based on tempo and audiosource sample rate
            ByteInterval = ConvertFromBpm(Bpm, AudioSource);
        }

        /**<summary>Convert a quarter note time value into a byte count.</summary>
         * <param name="bpm">Number of quarter-notes.</param>
         * <param name="src">The audio source to get the bytes/second from.</param>
         */
        static public double ConvertFromBpm(double bpm, IStreamProvider src)
        {
            double result = bpm * (60d / Metronome.GetInstance().Tempo) * src.WaveFormat.SampleRate;
            
            return result;
        }

        /**<summary>Constructor</summary>
         * <param name="beat">The numeric expression to be parsed.</param>
         * <param name="sourceName">The name of the audio source used by this cell.</param>
         */
        public BeatCell(string beat, string sourceName = "")
        {
            SourceName = sourceName;
            Bpm = Parse(beat);

            // is it a hihat closed or open sound?
            if (HiHatOpenFileNames.Contains(sourceName))
            {
                IsHiHatOpen = true;
            }
            else if (HiHatClosedFileNames.Contains(sourceName))
            {
                IsHiHatClosed = true;
            }
        }

        /**<summary>A list of the HiHat open sound file names.</summary>*/
        static public string[] HiHatOpenFileNames = new string[]
        {
            "wav/hihat_half_center_v4.wav",
            "wav/hihat_half_center_v7.wav",
            "wav/hihat_half_center_v10.wav",
            "wav/hihat_half_edge_v7.wav",
            "wav/hihat_half_edge_v10.wav",
            "wav/hihat_open_center_v4.wav",
            "wav/hihat_open_center_v7.wav",
            "wav/hihat_open_center_v10.wav",
            "wav/hihat_open_edge_v7.wav",
            "wav/hihat_open_edge_v10.wav"
        };

        /**<summary>A list of the HiHat closed sound file names.</summary>*/
        static public string[] HiHatClosedFileNames = new string[]
        {
            "wav/hihat_pedal_v3.wav",
            "wav/hihat_pedal_v5.wav"
        };

        /**<summary>Parse a math expression string.</summary>*/
        static public double Parse(string str)
        {
            string operators = "";
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '+' || str[i] == '-' || str[i] == '*' || str[i] == '/')
                    operators += str[i];
            }
            double[] numbers = str.Split(new char[] { '+', '-', '*', '/' }).Select((x) => Convert.ToDouble(x)).ToArray();

            // do mult and div
            while (operators.IndexOfAny(new[] { '*', '/' }) > -1)
            {
                int index = operators.IndexOfAny(new[] { '*', '/' });

                switch (operators[index])
                {
                    case '*':
                        numbers[index] *= numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                    case '/':
                        numbers[index] /= numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                }
                operators = operators.Remove(index, 1);
            }
            // do addition and subtraction
            while (operators.IndexOfAny(new[] { '+', '-' }) > -1)
            {
                int index = operators.IndexOfAny(new[] { '+', '-' });

                switch (operators[index])
                {
                    case '+':
                        numbers[index] += numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                    case '-':
                        numbers[index] -= numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                }
                operators = operators.Remove(index, 1);
            }

            return numbers[0];
        }
    }
}
