"""Render actual CDP screencasts with localized narration and editable captions.
Recorded timing is preserved; the final frame is held when narration is longer.
No synthetic success screens or replaced product text are used.
"""
from pathlib import Path
import argparse, html, json, math, re, subprocess, time, wave
from PIL import Image, ImageDraw, ImageFont
import win32com.client

p=argparse.ArgumentParser();p.add_argument('recording',type=Path);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
base=a.recording.resolve();out=a.output.resolve()
if any(x.lower()=='users' for x in out.parts): raise ValueError('Output outside user profiles required')
out.mkdir(parents=True,exist_ok=True)
ff=Path('C:/Development/XAIHT/Tlamatini/python/share/ffpyplayer/ffmpeg/bin/ffmpeg.exe')
probe=ff.with_name('ffprobe.exe')
chapters=json.loads((base/'chapters.json').read_text(encoding='utf8'))
results=json.loads((base/'results.json').read_text(encoding='utf8'))
if results['failed'] or len(chapters)!=24: raise ValueError('Record both verified language runs before rendering')
W,H=1600,1080
font=lambda n,b=False:ImageFont.truetype('C:/Windows/Fonts/'+('segoeuib.ttf' if b else 'segoeui.ttf'),n)
def wrapped(draw,text,f,width):
    lines=[];line=''
    for word in text.split():
        trial=(line+' '+word).strip()
        if draw.textlength(trial,font=f)>width and line: lines.append(line);line=word
        else: line=trial
    if line: lines.append(line)
    return lines
def stamp(seconds,sep=','):
    n=round(seconds*1000);return f'{n//3600000:02}:{n//60000%60:02}:{n//1000%60:02}{sep}{n%1000:03}'
def run(cmd,log):
    with log.open('w',encoding='utf8') as f:
        child=subprocess.Popen([str(x) for x in cmd],stdout=f,stderr=subprocess.STDOUT)
        last=time.monotonic()
        while child.poll() is None:
            time.sleep(.5)
            if time.monotonic()-last>8: print('Encoding / validating in progress...',flush=True);last=time.monotonic()
    if child.returncode: raise RuntimeError(log.read_text(encoding='utf8',errors='replace')[-5000:])
def narration(text,language,file):
    v=win32com.client.Dispatch('SAPI.SpVoice');voices=v.GetVoices();wanted='Sabina' if language=='es' else 'Zira'
    token=next((voices.Item(i) for i in range(voices.Count) if wanted in voices.Item(i).GetDescription()),None)
    if token is None: raise RuntimeError('Required native language voice unavailable: '+wanted)
    v.Voice=token;v.Rate=0;v.Volume=100
    stream=win32com.client.Dispatch('SAPI.SpFileStream');stream.Format.Type=22;stream.Open(str(file),3,False)
    v.AudioOutputStream=stream;v.Speak(text);stream.Close()
    with wave.open(str(file)) as w: return w.getnframes()/w.getframerate()
def frame(source,target,chapter,index,language):
    shot=Image.open(source).convert('RGB');image=Image.new('RGB',(W,H),'#091820');d=ImageDraw.Draw(image)
    d.rectangle((0,0,W,6),fill='#8ce0cf')
    d.text((40,24),'TLAMATINI RC  /  '+('LABORATORIO DE DESARROLLO' if language=='es' else 'DEVELOPMENT LAB'),font=font(20,True),fill='#8ce0cf')
    d.text((40,58),f'{index+1:02}  '+chapter['title'],font=font(33,True),fill='#f2f8fb')
    scale=min(1520/shot.width,850/shot.height);shot=shot.resize((round(shot.width*scale),round(shot.height*scale)),Image.Resampling.LANCZOS)
    x=(W-shot.width)//2;y=119+(850-shot.height)//2;image.paste(shot,(x,y));d.rectangle((x-1,y-1,x+shot.width,y+shot.height),outline='#38515d',width=1)
    label=('Grabación real · Datos de prueba · Pausas ampliadas para la narración' if language=='es' else 'Actual screen recording · Test data · Final frames held for narration')
    d.text((40,993),label,font=font(21),fill='#b7cbd3')
    d.text((40,1028),('Subtítulos y capítulos disponibles en el reproductor' if language=='es' else 'Captions and chapters available in the player'),font=font(20),fill='#8ce0cf')
    image.save(target,quality=91)

all_meta={}
for language in ['en','es']:
    selected=[c for c in chapters if c['language']==language];work=out/('work-'+language);work.mkdir(exist_ok=True)
    timeline=[];srt=[];segments=[];cursor=0.0
    for i,c in enumerate(selected):
        print(f'{language.upper()} {i+1}/{len(selected)}: {c["title"]}',flush=True)
        folder=work/c['id'];folder.mkdir(exist_ok=True);audio=folder/'voice.wav';seconds=narration(c['narration'],language,audio)
        duration=math.ceil(max(c['duration']+.4,seconds+1.1)*24)/24
        listed=[];frames=c['frames']
        for j,f in enumerate(frames):
            target=folder/f'{j:06}.jpg';frame(Path(c['dir'])/f['file'],target,c,i,language)
            end=frames[j+1]['time'] if j+1<len(frames) else duration
            dt=max(.001,end-f['time']);listed += ["file '"+target.as_posix()+"'",f'duration {dt:.6f}']
        listed.append("file '"+target.as_posix()+"'")
        concat=folder/'frames.txt';concat.write_text('\n'.join(listed),encoding='utf8')
        segment=folder/'chapter.mp4';segments.append(segment)
        run([ff,'-y','-hide_banner','-f','concat','-safe','0','-i',concat,'-i',audio,'-vf','fps=24','-af','apad','-t',f'{duration:.6f}','-c:v','libx264','-preset','veryfast','-crf','20','-pix_fmt','yuv420p','-c:a','aac','-ar','48000','-b:a','128k','-movflags','+faststart',segment],folder/'encode.log')
        # Caption sentences get a proportional share of the speech duration.
        sentences=re.split(r'(?<=[.!?])\s+',c['narration']);total=sum(len(s.split()) for s in sentences);subtime=cursor
        for sentence in sentences:
            length=seconds*len(sentence.split())/total;srt.append((subtime,subtime+length,sentence));subtime+=length
        timeline.append({'title':c['title'],'start':round(cursor,3),'duration':round(duration,3),'id':c['id']});cursor+=duration
    listfile=work/'segments.txt';listfile.write_text('\n'.join("file '"+s.as_posix()+"'" for s in segments),encoding='utf8')
    metadata=work/'chapters.txt';metadata.write_text(';FFMETADATA1\n'+''.join(f'[CHAPTER]\nTIMEBASE=1/1000\nSTART={round(c["start"]*1000)}\nEND={round((c["start"]+c["duration"])*1000)}\ntitle={c["title"]}\n' for c in timeline),encoding='utf8')
    name='Tlamatini-RC-'+('English' if language=='en' else 'Español');video=out/(name+'.mp4')
    run([ff,'-y','-hide_banner','-f','concat','-safe','0','-i',listfile,'-i',metadata,'-map','0','-map_metadata','1','-map_chapters','1','-c','copy','-movflags','+faststart',video],work/'final.log')
    (out/(name+'.srt')).write_text('\n\n'.join(f'{i+1}\n{stamp(s)} --> {stamp(e)}\n{text}' for i,(s,e,text) in enumerate(srt)),encoding='utf8')
    (out/(name+'.vtt')).write_text('WEBVTT\n\n'+'\n\n'.join(f'{stamp(s,".")} --> {stamp(e,".")}\n{text}' for s,e,text in srt),encoding='utf8')
    data=json.loads(subprocess.check_output([str(probe),'-v','error','-show_streams','-show_chapters','-show_format','-of','json',str(video)]))
    assert len(data['chapters'])==12
    assert any(s['codec_type']=='video' and s['codec_name']=='h264' and s['width']==W and s['height']==H for s in data['streams'])
    assert any(s['codec_type']=='audio' and s['codec_name']=='aac' for s in data['streams'])
    assert abs(float(data['format']['duration'])-cursor)<1
    run([ff,'-hide_banner','-v','error','-i',video,'-f','null','-'],work/'decode-validation.log')
    (out/(name+'-validation.json')).write_text(json.dumps(data,indent=2),encoding='utf8')
    all_meta[language]={'file':video.name,'captions':name+'.vtt','caption_text':(out/(name+'.vtt')).read_text(encoding='utf8'),'duration':round(cursor,2),'chapters':timeline}
    print(f'VALIDATED {video.name}: {cursor:.1f} seconds, 12 chapters, video + narration',flush=True)
(out/'videos.json').write_text(json.dumps(all_meta,ensure_ascii=False,indent=2),encoding='utf8')
(out/'verification.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf8')
payload=json.dumps(all_meta,ensure_ascii=False).replace('</','<\\/')
player='''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Tlamatini RC · Watch / Ver</title><style>*{box-sizing:border-box}body{margin:0;background:#0b1921;color:#edf7f9;font:18px 'Segoe UI',sans-serif}main{max-width:1200px;margin:auto;padding:24px}h1{font-size:30px}button{background:#89e2d1;color:#0b2528;border:0;border-radius:9px;padding:12px 20px;margin:4px;font:700 17px 'Segoe UI';cursor:pointer}video{display:block;width:100%;max-height:75vh;background:#000;border-radius:14px;margin-top:18px}#chapters{display:grid;grid-template-columns:repeat(auto-fit,minmax(250px,1fr));gap:5px;margin-top:18px}#chapters button{text-align:left;background:#1b3540;color:#edf7f9;font-size:15px}.note{color:#bfd0d6;line-height:1.5}a{color:#8de3d1}</style><main><h1>Tlamatini RC · Video walkthrough / Tutorial</h1><button onclick="select('en')">English</button><button onclick="select('es')">Español</button><p id="scope" class="note"></p><video id="video" controls preload="metadata"></video><div id="chapters"></div><p class="note" id="foot"></p><p><a href="verification.json">Recorded checks / Verificaciones grabadas</a></p></main><script>const data=DATA;const v=document.getElementById('video');function select(lang){const d=data[lang];document.documentElement.lang=lang;v.pause();v.replaceChildren();v.src=d.file;const track=document.createElement('track');track.kind='subtitles';track.label=lang==='es'?'Español':'English';track.srclang=lang;track.src=URL.createObjectURL(new Blob([d.caption_text],{type:'text/vtt'}));track.default=true;v.append(track);document.getElementById('scope').textContent=lang==='es'?'Recorrido del laboratorio actual: configuración, inicio de sesión, aprobación y muestras. No es acceso público por Internet. Las respuestas son datos de prueba.':'Current development lab: configuration, sign-in, approval and sample checks. This is not public Internet access. Responses use test data.';document.getElementById('foot').textContent=lang==='es'?'Grabación real con narración y subtítulos. Las pausas finales se amplían para explicar cada paso. Los controles de laboratorio son una herramienta de prueba, no una interfaz publicada.':'Actual screen recording with narration and captions. Final frames are held to explain each step. Lab controls are a test tool, not a released interface.';document.getElementById('chapters').replaceChildren(...d.chapters.map((c,i)=>{const b=document.createElement('button');b.textContent=String(i+1).padStart(2,'0')+' · '+c.title;b.onclick=()=>{v.currentTime=c.start;v.play();v.scrollIntoView({block:'center',behavior:'smooth'});};return b;}));v.load();}select('en');</script></html>'''.replace('DATA',payload)
(out/'Watch.html').write_text(player,encoding='utf8')
print('DONE: '+str(out/'Watch.html'),flush=True)
