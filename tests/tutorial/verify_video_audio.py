from pathlib import Path
from array import array
import json, math, wave, zipfile, sys

base=Path(sys.argv[1] if len(sys.argv)>1 else 'C:/Development/XAIHT/repo/Tlachihuani/artifacts/rc-hosted-video/watch').resolve()
evidence=[]
for language in ['en','es']:
    voices=list((base/('work-'+language)).glob('*/voice.wav'))
    assert len(voices)==12
    for file in voices:
        with wave.open(str(file)) as wav:
            assert wav.getsampwidth()==2 and wav.getnchannels()==1
            samples=array('h',wav.readframes(wav.getnframes()))
            rms=math.sqrt(sum(x*x for x in samples)/len(samples))
            peak=max(abs(x) for x in samples)
            assert rms>100 and peak>1000, 'Empty or inaudible narration'
            evidence.append({'language':language,'chapter':file.parent.name,'rms':round(rms,1),'peak':peak})
(base/'audio-verification.json').write_text(json.dumps(evidence,indent=2),encoding='utf8')
allowed=['Watch.html','videos.json','verification.json','player-verification.json','audio-verification.json']
allowed += [file.name for file in base.iterdir() if file.is_file() and file.suffix in ['.mp4','.srt','.vtt']]
archive=base.parent/'Tlamatini-RC-video-tutorials.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_STORED) as z:
    for name in allowed: z.write(base/name,name)
print('PASS: all 24 narration tracks contain non-silent audio.')
print('Packaged only finished videos, captions, player and verification; no credentials or browser profiles.')
print(archive)
