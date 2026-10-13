#!/usr/bin/env python3
"""Rebuilds the MVP placeholder WAVs from _sources/ into Assets/_Project/Audio/Placeholder/. Edits: trim, fade, high-pass (loop only),
loop crossfade (loop only), resample to 48 kHz, channel set, peak-normalise to -1 dBFS, 16-bit PCM."""
import subprocess, numpy as np, os
ROOT=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S=os.path.join(ROOT,'_sources'); SR=48000; TARGET_DB=-1.0
# ROOT is art-sources/audio/placeholder; the WAVs go into the Unity folder three levels up.
OUT=os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(ROOT))),'Assets','_Project','Audio','Placeholder')

def load(path, ch, start=0.0, dur=None, extra_af=None):
    af=[f'aresample={SR}:resampler=soxr']
    if extra_af: af.insert(0, extra_af)
    cmd=['ffmpeg','-v','error','-i',path,'-af',','.join(af),'-ac',str(ch),'-f','f32le','-']
    x=np.frombuffer(subprocess.run(cmd,capture_output=True,check=True).stdout,dtype=np.float32).reshape(-1,ch).copy()
    a=int(round(start*SR)); b=len(x) if dur is None else a+int(round(dur*SR))
    return x[a:b]

def fade(x, fin=0.005, fout=0.01):
    n=len(x); i=int(fin*SR); o=int(fout*SR)
    if i: x[:i]*=np.linspace(0,1,i)[:,None]
    if o: x[n-o:]*=(np.cos(np.linspace(0,np.pi/2,o))**2)[:,None]  # smooth fade-out
    return x

def loop_xfade(x, xf=1.0):
    n=int(xf*SR); t=np.linspace(0,np.pi/2,n)[:,None]
    head=x[:n]*np.sin(t); tail=x[-n:]*np.cos(t)          # equal-power
    y=x[:-n].copy(); y[:n]=head+tail
    return y

def write(name, x):
    pk=np.max(np.abs(x)); x=x*(10**(TARGET_DB/20)/pk)
    out=os.path.join(OUT,name)
    subprocess.run(['ffmpeg','-v','error','-y','-f','f32le','-ar',str(SR),'-ac',str(x.shape[1]),'-i','-',
                    '-c:a','pcm_s16le','-ar',str(SR),out],input=x.astype(np.float32).tobytes(),check=True)
    print(f'{name:22s} {len(x)/SR:6.3f}s ch={x.shape[1]}')

K=os.path.join(S,'kenney_ui-audio','Audio'); F=os.path.join(S,'freesound')
# 1 ui_click: Kenney UI Audio click1.ogg, mono, whole file
write('ui_click.wav', fade(load(os.path.join(K,'click1.ogg'),1),0.0,0.01))
# 2 order_ack: walkie-talkie key release, 0.00-0.29 s, mono
write('order_ack.wav', fade(load(os.path.join(F,'fs760245_LukaCafuka_preview-hq.ogg'),1,0.0,0.29),0.002,0.02))
# 3 raise_battalion: squad marching, 0.25-2.75 s (4 steps)
write('raise_battalion.wav', fade(load(os.path.join(F,'fs393019_josecruz98_preview-hq.ogg'),2,0.25,2.5),0.01,0.25))
# 4 depot_built: final sledgehammer strike on steel stake, 12.45-13.45 s
write('depot_built.wav', fade(load(os.path.join(F,'fs351312_petebuchwald_preview-hq.ogg'),2,12.45,1.0),0.003,0.45))
# 5 combat_loop: battlefield ambience, 30 Hz high-pass, 1.0 s equal-power loop crossfade
write('combat_loop.wav', loop_xfade(load(os.path.join(F,'fs385895_banan__preview-hq.ogg'),2,0.0,None,'highpass=f=30:poles=2'),1.0))
# 6 town_captured: closing bar of military snare cadence + reverb tail, 7.92-10.22 s
write('town_captured.wav', fade(load(os.path.join(F,'fs475246_aUREa_preview-hq.ogg'),2,7.92,2.3),0.005,0.4))
# 7 town_lost: last strikes of funeral bell peal, 48.40-52.40 s
write('town_lost.wav', fade(load(os.path.join(F,'fs454855_SamuelGremaud_preview-hq.ogg'),2,48.40,4.0),0.02,1.2))
# 8 win_stinger: opening phrase of Reveille (solo trumpet), 0.00-6.00 s
write('win_stinger.wav', fade(load(os.path.join(F,'fs651200_trumpetplayer_preview-hq.ogg'),2,0.0,6.0),0.005,0.25))
# 9 lose_stinger: opening phrase of Taps (solo trumpet), 0.15-6.00 s
write('lose_stinger.wav', fade(load(os.path.join(F,'fs651204_trumpetplayer_preview-hq.ogg'),2,0.15,5.85),0.02,0.3))
# 10 draw_stinger: same Taps phrase cut short, 0.15-3.25 s, long fade on the held note
write('draw_stinger.wav', fade(load(os.path.join(F,'fs651204_trumpetplayer_preview-hq.ogg'),2,0.15,3.10),0.02,1.0))
# 11 under_attack: Kenney UI Audio switch2, whole file, mono. Played at 0.8 by the HUD.
write('under_attack.wav', fade(load(os.path.join(K,'switch2.ogg'),1),0.0,0.02))
# 12 research_done: Kenney UI Audio switch3, whole file, mono.
write('research_done.wav', fade(load(os.path.join(K,'switch3.ogg'),1),0.0,0.02))
# 13 order_deny: Kenney UI Audio click2, a different click from ui_click (click1).
write('order_deny.wav', fade(load(os.path.join(K,'click2.ogg'),1),0.0,0.01))
