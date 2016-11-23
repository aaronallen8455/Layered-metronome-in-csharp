using System;

namespace Pronome
{
    class Program
    {
        static void Main(string[] args)
        {
            // TODO: Main runs in a different thread than the audio streams. Deal with thread safety.

            // if a future hihat closed sound is not muted, calculate its position in byte time, var hhc.
            // (number of cycles * cycle size + value of old byteInterval at start of present cycle + new byteInterval)
            // in the hihat open sound thread, determine how many bytes until hhc is reached.
            Metronome metronome = Metronome.GetInstance();
            metronome.Tempo = 75f;

            //new Layer("[.25,.25@C4,.25@A2,.25@A3,.25@Bb2,1.75@Bb3]2,[.25@F2,.25@F3,.25@D2,.25@D3,.25@Eb2,1.75@Eb3](2)-.5,1/6@Eb3,1/6@D3,1/6@Db3,.5@C3,.5@Eb3,.5@D3,.5@Ab2,.5@G2,.5@Db3,1/6@C3,1/6@F#3,1/6@F3,1/6@E3,1/6@Bb3,1/6@A3,1/3@Ab3,1/3@Eb3,1/3@B2,1/3@Bb2,1/3@A2,1/3+3@Ab2", "C3");
            //new Layer("[.5,.5+.75,.5,.75]4,.5,.25,.5,.25,.75,.25,.5,!1!.5,.5+.75,.5,.75!2!,.5,.75,.5,1,.25", WavFileStream.GetFileByName("Kick Drum V2"));
            //new Layer("[[1@0,1.5|.5]2,.25(2)]2,1@0,2,!1!1@0,.5,1.25!2.75!,.5,.75,1.5,.25(2)", WavFileStream.GetFileByName("Snare Rim V3"));
            //new Layer(".5(18),3/5(10)", WavFileStream.GetFileByName("HiHat Half Center V1"));

            new Layer(".5@0,[.5(3),.5@47,.25,.5,1.25]2,!3!.75,1,.75,.5@47,.25,1.5,!4!.5@47,1.5,.5,.25,{$s}2/3,{$s}4/5", WavFileStream.GetFileByName("Snare Center V2"));
            new Layer("[.75,3.25]3,.75,1.25,.75,1.25,{$s}2/3,{$s}4/5", WavFileStream.GetFileByName("Kick Drum V3"));
            new Layer("3*4+.25@0,.25@28,3.5,{$s}2/3,{$s}4/5", WavFileStream.GetFileByName("FloorTom V2"));
            new Layer(".25@0,[1,.5,.25@20,.25@22,.25@20,.5@22,.5,.25,.5]2,!3!.75,.5,1/6,1/6@12(2),.5,.25,.5,.75+1,!4!.5,.75,.25,.5(2),.5,{$s}2/3,{$s}4/5", WavFileStream.GetFileByName("HiHat Half Center V2"));

            //new Layer("[.75@0,.75,.5,2]3,.75@0,.75,.5+.75,.75,.5", "A4");
            //new Layer("1", "D3");

            //metronome.SetSilentInterval(4, 4);
            //Metronome.Save("Slinky");
            //todo: why does @0 on first of pitch layer get played?
            //var layer1 = new Layer("[1,2/3,1/3]4,{$s}2/3", "A4");
            //new Layer("1", "A5");
            //var layer2 = new Layer("1,2/3,1/3", WavFileStream.GetFileByName("Ride Center V3"));
            //new Layer("1,1,1/3@19", WavFileStream.GetFileByName("HiHat Pedal V2"));
            //Metronome.Load("metronome");
            //var metronome = Metronome.GetInstance();
            //metronome.SetRandomMute(50);

            
            metronome.Play();
            //metronome.Record("test");
            //metronome.ExportAsWav(0, "test.wav");
            Console.ReadKey();
            metronome.Stop();

            //metronome.ChangeTempo(50f);
            //Console.ReadKey();
            //metronome.ChangeTempo(38f);
            //Console.ReadKey();
            //metronome.Stop();
            //metronome.Play();
            //Console.ReadKey();
            //metronome.Stop();
            metronome.Dispose();

            //Console.ReadKey();
        }
    }
}