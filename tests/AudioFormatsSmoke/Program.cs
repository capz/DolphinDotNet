using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Audio;
using Dolphin.Storage;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

class Program
{
    static async Task<int> Main()
    {
        if(!Storage.Mount(StorageDevice.SdSerialPort2))return 1;
        Audio.Initialize();
        var hps=Sound.LoadGameCubeAsync("sd:/audio/tone.hps");
        var waves=Sound.LoadGameCubeAsync("sd:/audio/wave-1.aaf",GameCubeAudioFormat.JAudioWaveBank,"sd:/audio/wave-1.aw");
        var re4=Sound.LoadGameCubeAsync("sd:/audio/tone.sbh",GameCubeAudioFormat.ResidentEvil4Stream,"sd:/audio/tone.sbb");
        var rw=Sound.LoadGameCubeAsync("sd:/audio/tone.rws");
        var awd=Sound.LoadGameCubeAsync("sd:/audio/tone.awd",GameCubeAudioFormat.RenderWareWaveDictionary);
        await Task.WhenAll(new Task[]{hps,waves,re4,rw,awd});
        using(var dictionary=await awd){if(dictionary.SampleCount!=28)return 15;}
        using(var h=await hps)using(var w=await waves)using(var r=await re4)using(var s=await rw)
        {
            if(!Scheduler.IsApplicationThread||h.SampleRate!=22050||h.Channels!=2||h.SampleCount!=28||h.LengthBytes!=112||!h.HasLoop||h.LoopStartSample!=14||h.LoopEndSample!=28)return 2;
            if(w.SampleCount!=32||r.SampleCount!=28||s.Channels!=2||h.NativeBytes<224)return 3;
            using(var one=h.Play(true))using(var two=h.Play(true))
            {
                if(!one.IsPlaying||!two.IsPlaying)return 4;
                one.Volume=.5f;two.Pitch=1.25f;one.Stop();if(one.IsPlaying||!two.IsPlaying)return 5;
            }
        }
        using(var music=await Music.LoadGameCubeAsync("sd:/audio/tone.afs",entryIndex:1))
        {
            music.Play();if(!music.IsPlaying)return 6;
            music.PlayLoop(3,40);if(!music.IsPlaying)return 19;
            try{music.PlayLoop(40,3);return 20;}catch(ArgumentOutOfRangeException){}
            if(!music.IsPlaying)return 21;
            music.Volume=.3f;music.Pitch=.8f;music.Stop();if(music.IsPlaying)return 7;
        }
        try{await Sound.LoadGameCubeAsync("sd:/audio/bad-predictor.dsp",GameCubeAudioFormat.Dsp);return 8;}catch(IOException){}
        try{await Sound.LoadGameCubeAsync("sd:/audio/tone.sbh",GameCubeAudioFormat.ResidentEvil4Stream,"sd:/audio/missing.sbb");return 9;}catch(IOException){}
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();var task=Sound.LoadGameCubeAsync("sd:/audio/tone.hps",cancellationToken:cancel.Token);
            try{await task;return 10;}catch(OperationCanceledException){}
            if(!task.IsCanceled||!Scheduler.IsApplicationThread)return 11;
        }
        // Cancel between native read/validation slices; ownership must be released.
        using(var cancel=new CancellationTokenSource())
        {
            var task=Sound.LoadGameCubeAsync("sd:/audio/large.dsp",GameCubeAudioFormat.Dsp,cancellationToken:cancel.Token);
            await Task.Yield();cancel.Cancel();
            try{using(var unexpected=await task){}return 12;}catch(OperationCanceledException){}
        }
        using(var recovered=await Sound.LoadGameCubeAsync("sd:/audio/tone.dsp",GameCubeAudioFormat.Dsp))
        using(var channel=recovered.Play()){if(!channel.IsPlaying)return 13;}
        using(var background=await Task.Run(()=>Sound.LoadGameCubeAsync("sd:/audio/tone.adx").Result))
        {
            if(!Scheduler.IsApplicationThread||background.Channels!=2)return 14;
            using(var one=background.PlayLoop(1,33))using(var two=background.PlayLoop(31,64))
            {
                if(!one.IsPlaying||!two.IsPlaying||background.LoopStartSample!=3||background.LoopEndSample!=40)return 22;
                try{background.PlayLoop(-1,33);return 23;}catch(ArgumentOutOfRangeException){}
                try{background.PlayLoop(0,65);return 24;}catch(ArgumentOutOfRangeException){}
                try{background.PlayLoop(3,3);return 25;}catch(ArgumentOutOfRangeException){}
                one.Stop();if(!two.IsPlaying)return 26;
                int worker=await Task.Run(()=>{try{background.PlayLoop(3,40);return 1;}catch(InvalidOperationException){return 0;}});
                if(worker!=0)return 27;
            }
        }
        using(var sync=Sound.LoadGameCube("sd:/audio/tone.dsp",GameCubeAudioFormat.Dsp))
        {
            if(sync.SampleCount!=28)return 16;
            int worker=await Task.Run(()=>{try{sync.Play();return 1;}catch(InvalidOperationException){return 0;}});
            if(worker!=0)return 17;
        }
        using(var wav=await Sound.LoadAsync("sd:/audio/tone.wav"))
        {
            if(wav.SampleRate!=22050||wav.Channels!=1||wav.SampleCount!=32)return 18;
            try{wav.PlayLoop(1,20);return 28;}catch(NotSupportedException){}
        }
        return 0;
    }
}
