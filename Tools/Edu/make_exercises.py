"""Writes the training exercises (EDU_PLAN section 4) as activity files and their texts.

    python -I Tools/Edu/make_exercises.py

The exercises are data: this script only saves typing them by hand and keeps their texts in one place. It writes
Assets/StreamingAssets/Edu/Activities/<id>.json and the sheet Assets/_Project/Localization/Text/edu-training.tsv
(English key, Kazakh, Russian). Russian was written first; the Kazakh column needs a native review.
After running it: Ushqyr > Localization > Import Strings, then fly each new exercise's reference flight.
"""
import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / 'Assets' / 'StreamingAssets' / 'Edu' / 'Activities'
SHEET = REPO / 'Assets' / '_Project' / 'Localization' / 'Text' / 'edu-training.tsv'

TEXTS = {}  # English -> (Kazakh, Russian)


def t(en, kk, ru):
    """Registers a text and returns its key (the English)."""
    if en in TEXTS and TEXTS[en] != (kk, ru):
        raise SystemExit(f'two translations for: {en}')
    TEXTS[en] = (kk, ru)
    return en


CAMERA = {'place': 'field', 'drone': 'edu-camera900', 'assist': 'position', 'view': 'chase'}
PAD = {'type': 'pad', 'id': 'home', 'x': 0, 'y': 0, 'z': 0, 'size': 1.5}


def box(id, x, y, z, size=2, height=1):
    return {'type': 'box', 'id': id, 'x': x, 'y': y, 'z': z, 'size': size, 'height': height}


def zone(x, y, z, radius=1, tolerance=0.5):
    return {'x': x, 'y': y, 'z': z, 'radius': radius, 'tolerance': tolerance}


# Rubric lines used by several exercises.
def steps_line(steps, weight):
    return {'id': 'steps', 'metric': 'steps_done', 'label': t('Steps done', 'Орындалған қадамдар', 'Выполненные шаги'),
            'tip': t('Do the steps in the order shown at the top of the screen; each one counts only when the one before it is done.',
                     'Қадамдарды экранның жоғарғы жағында көрсетілген ретпен орындаңыз; әрқайсысы алдыңғысы орындалғанда ғана есептеледі.',
                     'Выполняйте шаги в том порядке, который показан вверху экрана; каждый засчитывается только после предыдущего.'),
            # Fewer than half of the steps earns nothing, and the line is required: an exercise cannot be passed on
            # its side marks alone by a flight that did not do it.
            'weight': weight, 'best': len(steps), 'worst': len(steps) // 2, 'required': True, 'params': {'steps': steps}}


def landing_line(weight, x=0, z=0):
    return {'id': 'landing', 'metric': 'landing_distance', 'label': t('Landing on the pad', 'Алаңшаға қону', 'Посадка на площадку'),
            'tip': t('Stop above the pad first, then come straight down.', 'Алдымен алаңшаның үстінде тоқтаңыз, содан кейін тік төмен түсіңіз.',
                     'Сначала остановитесь над площадкой, затем снижайтесь строго вниз.'),
            'weight': weight, 'best': 0.25, 'worst': 1.5, 'required': True, 'params': {'x': x, 'z': z}}


def smooth_line(weight):
    return {'id': 'smooth', 'metric': 'smoothness', 'label': t('Smooth sticks', 'Стиктерді бірқалыпты қозғау', 'Плавная работа стиками'),
            'tip': t('Move the sticks slowly and a little at a time.', 'Стиктерді баяу әрі аз-аздан қозғаңыз.', 'Двигайте стики медленно и понемногу.'),
            'weight': weight, 'best': 1.5, 'worst': 8}


def height_line(weight, y, best=0.15, worst=0.8, **window):
    return {'id': 'height', 'metric': 'height_error', 'label': t('Keeping the height', 'Биіктікті сақтау', 'Удержание высоты'),
            'tip': t('Watch the height while you hover: small, slow moves of the left stick.', 'Қалықтап тұрғанда биіктікті қадағалаңыз: сол стикті аз-аздан, баяу қозғаңыз.',
                     'Следите за высотой во время висения: небольшие, медленные движения левого стика.'),
            'weight': weight, 'best': best, 'worst': worst, 'params': dict({'y': y}, **window)}


def time_line(weight, best, worst):
    return {'id': 'time', 'metric': 'duration', 'label': t('Time taken', 'Жұмсалған уақыт', 'Затраченное время'),
            'tip': t('You have the moves; now do them without long pauses.', 'Қимылдарды меңгердіңіз; енді оларды ұзақ үзіліссіз орындаңыз.',
                     'Движения вы освоили; теперь выполняйте их без долгих пауз.'),
            'weight': weight, 'best': best, 'worst': worst}


LAND = {'say': t('Land on the pad and switch the motors off', 'Алаңшаға қонып, қозғалтқыштарды өшіріңіз', 'Сядьте на площадку и выключите моторы'), 'landed': True}

EXERCISES = []

# ---------------------------------------------------------------- A1
steps = [
    {'say': t('Left stick all the way up (more throttle)', 'Сол стикті соңына дейін жоғары (газды көбейту)', 'Левый стик до упора вверх (больше газа)'), 'stick': 'throttle', 'above': 0.8},
    {'say': t('Left stick all the way down (less throttle)', 'Сол стикті соңына дейін төмен (газды азайту)', 'Левый стик до упора вниз (меньше газа)'), 'stick': 'throttle', 'below': 0.2},
    {'say': t('Left stick to the left (turn left)', 'Сол стикті солға (солға бұрылу)', 'Левый стик влево (поворот влево)'), 'stick': 'yaw', 'below': -0.7},
    {'say': t('Left stick to the right (turn right)', 'Сол стикті оңға (оңға бұрылу)', 'Левый стик вправо (поворот вправо)'), 'stick': 'yaw', 'above': 0.7},
    {'say': t('Right stick up (fly forward)', 'Оң стикті жоғары (алға ұшу)', 'Правый стик вверх (лететь вперёд)'), 'stick': 'pitch', 'above': 0.7},
    {'say': t('Right stick down (fly backward)', 'Оң стикті төмен (артқа ұшу)', 'Правый стик вниз (лететь назад)'), 'stick': 'pitch', 'below': -0.7},
    {'say': t('Right stick to the left (slide left)', 'Оң стикті солға (солға жылжу)', 'Правый стик влево (смещение влево)'), 'stick': 'roll', 'below': -0.7},
    {'say': t('Right stick to the right (slide right)', 'Оң стикті оңға (оңға жылжу)', 'Правый стик вправо (смещение вправо)'), 'stick': 'roll', 'above': 0.7},
]
EXERCISES.append({
    'format': 1, 'id': 'a01-controls', 'type': 'exercise',
    'title': t('Controls check', 'Басқаруды тексеру', 'Проверка управления'),
    'briefing': t('The drone stays on the ground with its motors off. Move each stick as the screen asks, all the way and back to the middle. This is how you learn which stick does what.',
                  'Дрон қозғалтқыштары өшірулі күйде жерде тұрады. Әр стикті экранда сұралғандай соңына дейін қозғап, ортаға қайтарыңыз. Осылай қай стик не істейтінін үйренесіз.',
                  'Дрон стоит на земле с выключенными моторами. Двигайте каждый стик так, как просит экран: до упора и обратно в центр. Так вы запомните, что делает каждый стик.'),
    'pass': 60, 'time_limit': 120,
    'setup': dict(CAMERA, on_the_ground=True, objects=[PAD], end={'landing': False, 'crash': False}),
    'rubric': [steps_line(steps, 80), time_line(20, 25, 90)],
    'reference': [c for move in [{'throttle': 1}, {'throttle': 0}, {'throttle': 0, 'yaw': -1}, {'throttle': 0, 'yaw': 1}, {'throttle': 0, 'pitch': 1},
                                 {'throttle': 0, 'pitch': -1}, {'throttle': 0, 'roll': -1}, {'throttle': 0, 'roll': 1}]
                  for c in ({'sticks': move, 'wait': 0.6}, {'sticks': {'throttle': move.get('throttle', 0)}, 'wait': 0.5})],
})

# ---------------------------------------------------------------- A2
EXERCISES.append({
    'format': 1, 'id': 'a02-hover-land', 'type': 'exercise',
    'title': t('Take off, hover, land', 'Ұшып көтерілу, қалықтау, қону', 'Взлёт, висение, посадка'),
    'briefing': t('Take off and climb into the blue box, 2 m up. Stay inside it for 20 seconds. Then come down and land on the pad you started from.',
                  'Ұшып көтеріліп, 2 м биіктіктегі көк қорапқа кіріңіз. Оның ішінде 20 секунд тұрыңыз. Содан кейін төмендеп, ұшқан алаңшаңызға қоныңыз.',
                  'Взлетите и поднимитесь в синий куб на высоте 2 м. Оставайтесь в нём 20 секунд. Затем снизьтесь и сядьте на площадку, с которой взлетели.'),
    'pass': 60, 'time_limit': 90,
    'setup': dict(CAMERA, objects=[box('hover', 0, 2, 0, 2, 1), PAD]),
    'rubric': [
        {'id': 'hold', 'metric': 'hold_time', 'label': t('Time inside the box', 'Қорап ішіндегі уақыт', 'Время внутри куба'),
         'tip': t('Climb until the drone is inside the blue box, then let go of the right stick and only correct small drifts.',
                  'Дрон көк қораптың ішіне кіргенше көтеріліңіз, содан кейін оң стикті жіберіп, тек шағын ығысуларды түзетіңіз.',
                  'Поднимайтесь, пока дрон не окажется в синем кубе, затем отпустите правый стик и поправляйте только небольшой снос.'),
         'weight': 40, 'best': 20, 'worst': 0, 'required': True, 'params': {'x': 0, 'z': 0, 'radius': 1, 'y': 2, 'height_tolerance': 0.5}},
        height_line(15, 2, 0.1, 0.8, **{'from': 4, 'to': 24}),
        dict(landing_line(25), best=0.2),
        {'id': 'touch', 'metric': 'touchdown_speed', 'label': t('A soft landing', 'Жұмсақ қону', 'Мягкая посадка'),
         'tip': t('Slow the descent in the last metre.', 'Соңғы метрде төмендеуді баяулатыңыз.', 'Замедлите снижение на последнем метре.'),
         'weight': 10, 'best': 0.5, 'worst': 2.5},
        smooth_line(10),
    ],
    'reference': [{'takeoff': 2, 'wait': 26}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- A3
heights = [1, 3, 5, 2]
steps = [{'say': t('Climb or descend to %d m and hold it for 5 seconds' % h, '%d м биіктікке көтеріліп немесе төмендеп, 5 секунд ұстаңыз' % h, 'Наберите или снизьтесь до %d м и удерживайте 5 секунд' % h),
          'height': h, 'tolerance': 0.3, 'for': 5} for h in heights] + [LAND]
EXERCISES.append({
    'format': 1, 'id': 'a03-height-steps', 'type': 'exercise',
    'title': t('Height steps', 'Биіктік сатылары', 'Ступени высоты'),
    'briefing': t('Fly to four heights, one after another: 1 m, 3 m, 5 m and 2 m. Hold each for 5 seconds inside its box, without drifting sideways. Then land.',
                  'Төрт биіктікке бірінен соң бірін ұшыңыз: 1 м, 3 м, 5 м және 2 м. Әрқайсысын өз қорабының ішінде, бүйірге ығыспай, 5 секунд ұстаңыз. Содан кейін қоныңыз.',
                  'Пролетите четыре высоты одну за другой: 1 м, 3 м, 5 м и 2 м. На каждой продержитесь 5 секунд внутри её куба, не смещаясь в стороны. Затем сядьте.'),
    'pass': 60, 'time_limit': 150,
    'setup': dict(CAMERA, objects=[PAD] + [box('h%d' % h, 0, h, 0, 2, 0.6) for h in heights]),
    'rubric': [steps_line(steps, 50),
               {'id': 'drift', 'metric': 'position_error', 'label': t('Staying over the pad', 'Алаңша үстінде қалу', 'Удержание над площадкой'),
                'tip': t('Only the left stick moves in this exercise: keep your right thumb still.', 'Бұл жаттығуда тек сол стик қозғалады: оң бас бармағыңызды қозғамаңыз.',
                         'В этом упражнении двигается только левый стик: правый большой палец держите неподвижно.'),
                'weight': 20, 'best': 0.15, 'worst': 1.5, 'params': {'x': 0, 'z': 0}},
               landing_line(15), smooth_line(15)],
    'reference': [{'takeoff': 1, 'until_step': 1}, {'goto': [0, 3, 0], 'heading': 0, 'until_step': 2}, {'goto': [0, 5, 0], 'heading': 0, 'until_step': 3},
                  {'goto': [0, 2, 0], 'heading': 0, 'until_step': 4}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- A4
steps = [
    {'say': t('Take off and hover in the box, 2 m up', 'Ұшып көтеріліп, 2 м биіктіктегі қорапта қалықтаңыз', 'Взлетите и зависните в кубе на высоте 2 м'), 'zone': zone(0, 2, 0, 0.75), 'for': 2},
    {'say': t('Turn a full circle to the right, on the spot', 'Орнында тұрып, оңға толық айналым жасаңыз', 'Сделайте полный оборот вправо на месте'), 'turn': 360},
    {'say': t('Turn a full circle to the left, on the spot', 'Орнында тұрып, солға толық айналым жасаңыз', 'Сделайте полный оборот влево на месте'), 'turn': -360},
    LAND,
]
EXERCISES.append({
    'format': 1, 'id': 'a04-pirouette', 'type': 'exercise',
    'title': t('Pirouette', 'Пируэт', 'Пируэт'),
    'briefing': t('Hover in the small box and turn the drone once around to the right, then once around to the left, without leaving the box. Only the left stick turns the drone.',
                  'Шағын қорапта қалықтап тұрып, дронды бір рет оңға, содан кейін бір рет солға айналдырыңыз, қораптан шықпаңыз. Дронды тек сол стик бұрады.',
                  'Зависните в маленьком кубе и разверните дрон на полный оборот вправо, затем на полный оборот влево, не выходя из куба. Поворачивает дрон только левый стик.'),
    'pass': 60, 'time_limit': 120,
    'setup': dict(CAMERA, objects=[PAD, box('spot', 0, 2, 0, 1.5, 1)]),
    'rubric': [steps_line(steps, 50),
               {'id': 'drift', 'metric': 'position_error', 'label': t('Staying in the box while turning', 'Бұрылу кезінде қорапта қалу', 'Удержание в кубе при повороте'),
                'tip': t('Turn slowly. If the drone slides away, stop turning, bring it back, then go on.', 'Баяу бұрылыңыз. Дрон ығысып кетсе, бұрылуды тоқтатып, оны қайтарыңыз да, жалғастырыңыз.',
                         'Поворачивайте медленно. Если дрон уходит в сторону, остановите поворот, верните его и продолжайте.'),
                'weight': 30, 'best': 0.15, 'worst': 0.75, 'params': {'x': 0, 'z': 0}},
               height_line(10, 2), landing_line(10)],
    'reference': [{'takeoff': 2, 'until_step': 1}]
                 + [{'heading': h, 'wait': 3.5} for h in (100, 200, 300)] + [{'heading': 10, 'until_step': 2}]
                 + [{'heading': h, 'wait': 3.5} for h in (270, 180, 90)] + [{'heading': 350, 'until_step': 3}]
                 + [{'heading': 0, 'wait': 2}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- A5
steps = [
    {'say': t('Take off and hover in the near box, 2 m up', 'Ұшып көтеріліп, 2 м биіктіктегі жақын қорапта қалықтаңыз', 'Взлетите и зависните в ближнем кубе на высоте 2 м'), 'zone': zone(0, 2, 0), 'for': 2},
    {'say': t('Fly 15 m forward and stop in the far box for 3 seconds', '15 м алға ұшып, алыс қорапта 3 секунд тоқтаңыз', 'Пролетите 15 м вперёд и остановитесь в дальнем кубе на 3 секунды'), 'zone': zone(0, 2, 15), 'for': 3},
    {'say': t('Fly back and stop in the near box for 3 seconds', 'Кері ұшып, жақын қорапта 3 секунд тоқтаңыз', 'Вернитесь и остановитесь в ближнем кубе на 3 секунды'), 'zone': zone(0, 2, 0), 'for': 3},
    LAND,
]
EXERCISES.append({
    'format': 1, 'id': 'a05-out-and-back', 'type': 'exercise',
    'title': t('Out and back', 'Барып қайту', 'Туда и обратно'),
    'briefing': t('Fly straight ahead to the far box, 15 m away, and stop inside it. Then fly back, stop in the near box and land. Start slowing down before you reach a box.',
                  '15 м қашықтықтағы алыс қорапқа тура алға ұшып, оның ішінде тоқтаңыз. Содан кейін кері ұшып, жақын қорапта тоқтап, қоныңыз. Қорапқа жетпей тұрып баяулай бастаңыз.',
                  'Летите прямо к дальнему кубу в 15 м и остановитесь внутри него. Затем вернитесь, остановитесь в ближнем кубе и сядьте. Начинайте тормозить до того, как долетите до куба.'),
    'pass': 60, 'time_limit': 150,
    'setup': dict(CAMERA, objects=[PAD, box('near', 0, 2, 0), box('far', 0, 2, 15)]),
    'rubric': [steps_line(steps, 50),
               {'id': 'line', 'metric': 'path_error', 'label': t('Flying a straight line', 'Түзу сызықпен ұшу', 'Полёт по прямой'),
                'tip': t('Push the right stick straight up and down, not at an angle.', 'Оң стикті қиғаш емес, тура жоғары-төмен итеріңіз.', 'Двигайте правый стик строго вверх и вниз, не по диагонали.'),
                'weight': 15, 'best': 0.3, 'worst': 2, 'params': {'points': [[0, 2, 0], [0, 2, 15]], 'from': 6}},
               landing_line(20), time_line(15, 45, 110)],
    'reference': [{'takeoff': 2, 'until_step': 1}, {'goto': [0, 2, 15], 'heading': 0, 'until_step': 2}, {'goto': [0, 2, 0], 'heading': 0, 'until_step': 3},
                  {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- A6
corners = [(-4, 4), (4, 4), (4, 12), (-4, 12), (-4, 4)]
steps = []
for i, (x, z) in enumerate(corners):
    say = t('Fly to corner %d and stop for 2 seconds' % (i % 4 + 1) if i < 4 else 'Close the square: back to corner 1, stop for 2 seconds',
            '%d-бұрышқа ұшып, 2 секунд тоқтаңыз' % (i % 4 + 1) if i < 4 else 'Шаршыны жабыңыз: 1-бұрышқа қайтып, 2 секунд тоқтаңыз',
            'Летите к углу %d и остановитесь на 2 секунды' % (i % 4 + 1) if i < 4 else 'Замкните квадрат: вернитесь к углу 1 и остановитесь на 2 секунды')
    steps.append({'say': say, 'zone': zone(x, 2, z), 'for': 2})
steps.append(LAND)
EXERCISES.append({
    'format': 1, 'id': 'a06-square', 'type': 'exercise',
    'title': t('Square, tail towards you', 'Шаршы, құйрығы өзіңізге қарай', 'Квадрат хвостом к себе'),
    'briefing': t('Fly a square through four boxes and stop in each. Keep the drone facing away from you the whole time, so left is always left. Then come back and land on the pad.',
                  'Төрт қорап арқылы шаршы бойымен ұшып, әрқайсысында тоқтаңыз. Дрон үнемі сізден теріс қарап тұрсын, сонда сол жақ әрқашан сол жақ болады. Содан кейін қайтып, алаңшаға қоныңыз.',
                  'Пролетите квадрат через четыре куба, останавливаясь в каждом. Всё время держите дрон хвостом к себе, тогда лево всегда будет слева. Затем вернитесь и сядьте на площадку.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(CAMERA, objects=[PAD] + [box('c%d' % (i + 1), x, 2, z) for i, (x, z) in enumerate(corners[:4])]),
    'rubric': [steps_line(steps, 50),
               {'id': 'heading', 'metric': 'heading_error', 'label': t('Keeping the tail towards you', 'Құйрықты өзіңізге қаратып ұстау', 'Удержание хвостом к себе'),
                'tip': t('Leave the left stick in the middle from side to side: in this exercise the drone never turns.', 'Сол стикті оңға-солға қозғамай, ортасында ұстаңыз: бұл жаттығуда дрон бұрылмайды.',
                         'Не отклоняйте левый стик вправо-влево: в этом упражнении дрон не поворачивается.'),
                'weight': 20, 'best': 5, 'worst': 45, 'params': {'heading': 0}},
               height_line(10, 2, 0.2, 1.0, **{'from': 6}), landing_line(10), time_line(10, 70, 160)],
    'reference': [{'takeoff': 2, 'wait': 5}] + [{'goto': [x, 2, z], 'heading': 0, 'until_step': i + 1} for i, (x, z) in enumerate(corners)]
                 + [{'goto': [0, 2, 0], 'heading': 0, 'wait': 6}, {'land': True, 'until_landed': True}],
})


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for e in EXERCISES:
        (OUT / (e['id'] + '.json')).write_text(json.dumps(e, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
        print('wrote', e['id'], '-', len(e['rubric']), 'rubric lines')
    lines = ['# Education Suite: the training exercises (written by Tools/Edu/make_exercises.py; edit the texts there). '
             'Russian was written first; the Kazakh column needs a native review. Columns: English (key) <TAB> Kazakh <TAB> Russian']
    for en, (kk, ru) in TEXTS.items():
        for text in (en, kk, ru):
            if '\t' in text or '\n' in text:
                raise SystemExit('a text holds a tab or a line break: ' + en)
        lines.append('\t'.join((en, kk, ru)))
    SHEET.write_text('\n'.join(lines) + '\n', encoding='utf-8', newline='\n')
    print('wrote', len(TEXTS), 'texts to', SHEET.relative_to(REPO))


if __name__ == '__main__':
    main()
