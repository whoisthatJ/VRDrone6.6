"""Narration for the product video: Russian voice-over, with the name said by a Kazakh voice.

python Tools/Video/voice.py   -> Recordings/video/voice/sNN.wav (one per segment) and timing.json

Uses Microsoft's online neural voices through edge-tts (the narration text is sent to that service).
"""
import asyncio
import json
import os
import subprocess
import wave

import edge_tts
import imageio_ffmpeg

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Recordings', 'video', 'voice')
FF = imageio_ffmpeg.get_ffmpeg_exe()
RU, KK = 'ru-RU-DmitryNeural', 'kk-KZ-DauletNeural'
NAME = ('kk', 'Ұшқыр')

# Each segment: an id, the caption shown on screen, and the narration as (voice, text) parts spoken one after another.
SEGMENTS = [
    ('intro', 'Тренажёр операторов БПЛА', [
        NAME,
        ('ru', '— виртуальный тренажёр для операторов беспилотников. Здесь учатся летать до того, как получат в руки настоящий дрон.'),
    ]),
    ('physics', 'Настоящая физика полёта', [
        ('ru', 'В основе — настоящая физика полёта: тяга моторов, инерция, сопротивление воздуха и ветер. '
               'Летать можно с пульта, геймпада или клавиатуры — на обычном ноутбуке.'),
    ]),
    ('garage', 'Гараж: сборка дрона', [
        ('ru', 'В гараже дрон собирается из деталей реальных классов: рама, моторы, пропеллеры, батарея. '
               'Каждая деталь меняет характеристики, и это сразу чувствуется в полёте.'),
    ]),
    ('race', 'Гонки с ИИ-соперниками', [
        ('ru', 'Гоночные трассы — от стадиона до района ЭКСПО в Астане, построенного по открытым картам в реальном масштабе. '
               'Соперники с искусственным интеллектом летают на той же физике, что и человек.'),
    ]),
    ('strike', 'Ударный полигон', [
        ('ru', 'Ударный полигон: за две минуты оператор поражает учебные цели — неподвижные, движущиеся колонны и машины, которые уклоняются. '
               'Система считает очки, точность и серии, так что результат объективен.'),
    ]),
    ('online', 'Группа до 8 операторов', [
        ('ru', 'Группа до восьми операторов занимается вместе — через интернет или в классе по локальной сети. '
               'Интерфейс на каза\u0301хском, русском и английском.'),  # the stress mark: without it the voice says кАзахском
    ]),
    ('outro', 'Multimedia Lab · SDU University', [
        NAME,
        ('ru', 'Собственная разработка лаборатории мультимедиа университета Эс-Ди-Ю.'),
    ]),
]


def to_wav(src, dst):
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', src, '-ar', '48000', '-ac', '1', dst], check=True)


def trim(path):
    """Cuts leading and trailing silence, so parts join without gaps we did not ask for."""
    tmp = path + '.t.wav'
    f = 'silenceremove=start_periods=1:start_threshold=-45dB,areverse,silenceremove=start_periods=1:start_threshold=-45dB,areverse'
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', path, '-af', f, tmp], check=True)
    os.replace(tmp, path)


def read(path):
    with wave.open(path, 'rb') as w:
        return w.readframes(w.getnframes()), w.getframerate()


async def main():
    os.makedirs(OUT, exist_ok=True)
    timing = []
    for i, (sid, caption, parts) in enumerate(SEGMENTS):
        pcm = b''
        rate = 48000
        for j, (voice, text) in enumerate(parts):
            mp3 = os.path.join(OUT, f's{i:02d}_{j}.mp3')
            wav = os.path.join(OUT, f's{i:02d}_{j}.wav')
            # The name is said a little slower and stands on its own; then a short pause before the Russian carries on.
            for attempt in range(4):
                try:
                    await edge_tts.Communicate(text, KK if voice == 'kk' else RU, rate='-12%' if voice == 'kk' else '+4%').save(mp3)
                    break
                except edge_tts.exceptions.NoAudioReceived:
                    # The service drops a request now and then.
                    if attempt == 3:
                        raise
                    await asyncio.sleep(2)
            to_wav(mp3, wav)
            trim(wav)
            data, rate = read(wav)
            pcm += data + b'\x00\x00' * int(rate * (0.28 if voice == 'kk' else 0.0))
            os.remove(mp3)
            os.remove(wav)
        out = os.path.join(OUT, f's{i:02d}.wav')
        with wave.open(out, 'wb') as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(rate)
            w.writeframes(pcm)
        seconds = len(pcm) / 2 / rate
        timing.append({'id': sid, 'caption': caption, 'file': os.path.basename(out), 'seconds': round(seconds, 2)})
        print(sid, round(seconds, 2))
    json.dump(timing, open(os.path.join(OUT, 'timing.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('total', round(sum(t['seconds'] for t in timing), 1))


if __name__ == '__main__':
    asyncio.run(main())
