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


# ================================================================ Block B: orientation and precision
# Flown from where the pilot stands (line of sight), so the drone's own left and right no longer match the pilot's.
LOS = dict(CAMERA, view='los')
HOME = {'x': 0, 'z': 0, 'radius': 0.75}
LAND_HOME = {'say': t('Fly back, land on the pad and switch the motors off', 'Кері ұшып, алаңшаға қонып, қозғалтқыштарды өшіріңіз', 'Вернитесь, сядьте на площадку и выключите моторы'),
             'landed': True, 'pad': HOME}


def pole(id, x, z, height=3.5):
    return {'type': 'pole', 'id': id, 'x': x, 'y': 0, 'z': z, 'size': 0.25, 'height': height}


def path_line(weight, points, best=0.4, worst=2.5):
    return {'id': 'path', 'metric': 'path_error', 'label': t('Staying on the line', 'Сызық бойында қалу', 'Удержание линии'),
            'tip': t('Look at the next box, not at the drone, and fly straight at it.', 'Дронға емес, келесі қорапқа қарап, тура соған ұшыңыз.', 'Смотрите на следующий куб, а не на дрон, и летите прямо к нему.'),
            'weight': weight, 'best': best, 'worst': worst, 'params': {'points': points}}


# ---------------------------------------------------------------- B7
sq = [(-4, 4), (4, 4), (4, 12), (-4, 12)]
face = [90, 0, 270, 180]
steps, ref = [], [{'takeoff': 2, 'wait': 5}]
for i in range(5):
    x, z = sq[i % 4]
    n = i % 4 + 1
    steps.append({'say': t('Fly to corner %d and stop' % n if i < 4 else 'Close the square: fly back to corner 1',
                           '%d-бұрышқа ұшып, тоқтаңыз' % n if i < 4 else 'Шаршыны жабыңыз: 1-бұрышқа қайта ұшыңыз',
                           'Летите к углу %d и остановитесь' % n if i < 4 else 'Замкните квадрат: вернитесь к углу 1'),
                  'zone': zone(x, 2, z), 'for': 1})
    ref.append({'goto': [x, 2, z], 'heading': face[(i + 3) % 4] if i > 0 else 0, 'until_step': len(steps)})
    if i < 4:
        steps.append({'say': t('Turn the nose towards the next corner', 'Тұмсықты келесі бұрышқа бұрыңыз', 'Поверните нос к следующему углу'),
                      'heading': face[i], 'tolerance': 20, 'for': 1})
        ref.append({'heading': face[i], 'until_step': len(steps)})
steps.append(LAND_HOME)
ref += [{'goto': [0, 2, 0], 'heading': 0, 'wait': 7}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'b07-square-nose-forward', 'type': 'exercise',
    'title': t('Square, nose forward', 'Шаршы, тұмсығы алға', 'Квадрат носом вперёд'),
    'briefing': t('Fly the square again, but this time turn the drone at every corner so that its nose always points where it is going. When it flies towards you, left and right are swapped.',
                  'Шаршыны қайта ұшыңыз, бірақ бұл жолы әр бұрышта дронды бұрып, тұмсығы әрдайым ұшу бағытына қарап тұрсын. Ол сізге қарай ұшқанда, оң мен сол ауысады.',
                  'Пролетите квадрат снова, но теперь поворачивайте дрон на каждом углу, чтобы нос всегда смотрел по ходу движения. Когда он летит к вам, лево и право меняются местами.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(LOS, objects=[PAD] + [box('c%d' % (i + 1), x, 2, z) for i, (x, z) in enumerate(sq)]),
    'rubric': [steps_line(steps, 50), path_line(20, [[0, 2, 0], [-4, 2, 4], [4, 2, 4], [4, 2, 12], [-4, 2, 12], [-4, 2, 4], [0, 2, 0]]),
               height_line(10, 2, 0.2, 1.0, **{'from': 6}), landing_line(10), time_line(10, 100, 220)],
    'reference': ref,
})

# ---------------------------------------------------------------- B8
ring = {'x': 0, 'z': 10, 'near': 2.5, 'far': 7.5}
steps = [
    {'say': t('Fly to the box in front of the pole', 'Бағана алдындағы қорапқа ұшыңыз', 'Летите к кубу перед столбом'), 'zone': zone(0, 2, 5, 1.2), 'for': 1},
    {'say': t('Circle the pole once clockwise, nose on the pole', 'Тұмсықты бағанаға қаратып, бағананы сағат тілі бағытымен бір айналыңыз', 'Облетите столб один раз по часовой стрелке, носом к столбу'),
     'circle': dict(ring, degrees=360)},
    {'say': t('Now once the other way, nose still on the pole', 'Енді кері бағытта бір рет, тұмсық әлі бағанаға қарап тұрсын', 'Теперь один раз в обратную сторону, нос по-прежнему на столб'),
     'circle': dict(ring, degrees=-360)},
    LAND_HOME,
]
EXERCISES.append({
    'format': 1, 'id': 'b08-circle-a-pole', 'type': 'exercise',
    'title': t('Circle a pole', 'Бағананы айналу', 'Облёт столба'),
    'briefing': t('Fly a circle around the pole, 5 m from it, keeping the nose of the drone pointed at the pole all the way round. Then do it the other way. You need both sticks at the same time: one slides the drone sideways, the other turns it.',
                  'Бағанадан 5 м қашықтықта оны айнала ұшыңыз, дронның тұмсығы бүкіл жол бойы бағанаға қарап тұрсын. Содан кейін кері бағытта қайталаңыз. Екі стик бір мезгілде керек: бірі дронды бүйірге жылжытады, екіншісі бұрады.',
                  'Облетите столб по кругу в 5 м от него, всё время удерживая нос дрона на столбе. Затем повторите в другую сторону. Нужны оба стика одновременно: один смещает дрон вбок, другой поворачивает.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(LOS, objects=[PAD, pole('pole', 0, 10), box('start', 0, 2, 5)]),
    'rubric': [steps_line(steps, 40),
               {'id': 'radius', 'metric': 'radius_error', 'label': t('Keeping the distance from the pole', 'Бағанадан қашықтықты сақтау', 'Удержание расстояния до столба'),
                'tip': t('If the circle grows, push a little towards the pole; if it shrinks, pull away.', 'Шеңбер үлкейсе, бағанаға сәл жақындаңыз; кішірейсе, алыстаңыз.', 'Если круг растёт, чуть подайте дрон к столбу; если сжимается, отойдите.'),
                'weight': 20, 'best': 0.4, 'worst': 2, 'params': dict(ring, radius=5)},
               {'id': 'aim', 'metric': 'aim_error', 'label': t('Nose on the pole', 'Тұмсық бағанаға қараған', 'Нос на столб'),
                'tip': t('Turn with the left stick at the same pace as you slide with the right one.', 'Оң стикпен жылжыған қарқынмен сол стикпен бұрылыңыз.', 'Поворачивайте левым стиком в том же темпе, в каком смещаетесь правым.'),
                'weight': 20, 'best': 8, 'worst': 40, 'params': ring},
               height_line(10, 2, 0.2, 1.0, **{'from': 6}), landing_line(10)],
    'reference': [{'takeoff': 2, 'wait': 5}, {'goto': [0, 2, 5], 'heading': 0, 'until_step': 1},
                  {'orbit': [0, 10], 'radius': 5, 'speed': 2, 'until_step': 2}, {'orbit': [0, 10], 'radius': 5, 'speed': -2, 'until_step': 3},
                  {'goto': [0, 2, 0], 'heading': 0, 'wait': 8}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- B9
# The poles stand side by side, 12 m out, so the way from the pad to the box between them is clear.
one = {'x': -5, 'z': 12, 'near': 2.5, 'far': 7.5}
two = {'x': 5, 'z': 12, 'near': 2.5, 'far': 7.5}
MIDDLE = {'say': t('Come through the box between the poles', 'Бағаналар арасындағы қорап арқылы өтіңіз', 'Пройдите через куб между столбами'), 'zone': zone(0, 2, 12, 1.5)}
steps = [
    dict(MIDDLE, **{'for': 1}),
    {'say': t('Circle the left pole clockwise', 'Сол жақ бағананы сағат тілі бағытымен айналыңыз', 'Облетите левый столб по часовой стрелке'), 'circle': dict(one, degrees=360)},
    MIDDLE,
    {'say': t('Circle the right pole the other way', 'Оң жақ бағананы кері бағытта айналыңыз', 'Облетите правый столб в обратную сторону'), 'circle': dict(two, degrees=-360)},
    MIDDLE,
    LAND_HOME,
]
EXERCISES.append({
    'format': 1, 'id': 'b09-figure-eight', 'type': 'exercise',
    'title': t('Figure of eight', 'Сегіздік', 'Восьмёрка'),
    'briefing': t('Fly a figure of eight around the two poles: once round the left pole, through the box in the middle, then round the right pole the other way. Keep the height the same all the way.',
                  'Екі бағананы айнала сегіздік жасаңыз: сол жақ бағананы бір айналып, ортадағы қорап арқылы өтіп, оң жақ бағананы кері бағытта айналыңыз. Биіктікті бүкіл жол бойы бірдей ұстаңыз.',
                  'Пролетите восьмёрку вокруг двух столбов: один раз вокруг левого, через куб посередине, затем вокруг правого в обратную сторону. Высоту держите одинаковой весь путь.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(LOS, objects=[PAD, pole('left', -5, 12), pole('right', 5, 12), box('middle', 0, 2, 12, 3, 1)]),
    'rubric': [steps_line(steps, 45),
               {'id': 'left', 'metric': 'radius_error', 'label': t('The circle round the left pole', 'Сол жақ бағана айналасындағы шеңбер', 'Круг вокруг левого столба'),
                'tip': t('Start each circle from the middle box at an easy speed; a fast entry makes a wide circle.', 'Әр шеңберді ортадағы қораптан баяу бастаңыз; жылдам кіру шеңберді кеңейтеді.', 'Начинайте каждый круг из среднего куба на небольшой скорости; быстрый вход делает круг широким.'),
                'weight': 15, 'best': 0.5, 'worst': 2.5, 'params': dict(one, radius=5)},
               {'id': 'right', 'metric': 'radius_error', 'label': t('The circle round the right pole', 'Оң жақ бағана айналасындағы шеңбер', 'Круг вокруг правого столба'),
                'tip': t('The second circle turns the other way: slow down in the middle box before you change direction.', 'Екінші шеңбер кері бағытта: бағытты өзгертпес бұрын ортадағы қорапта баяулаңыз.', 'Второй круг идёт в другую сторону: притормозите в среднем кубе, прежде чем менять направление.'),
                'weight': 15, 'best': 0.5, 'worst': 2.5, 'params': dict(two, radius=5)},
               height_line(10, 2, 0.2, 1.0, **{'from': 6}), landing_line(15)],
    'reference': [{'takeoff': 2, 'wait': 5}, {'goto': [0, 2, 12], 'heading': 0, 'until_step': 1},
                  {'orbit': [-5, 12], 'radius': 5, 'speed': 2, 'until_step': 3}, {'orbit': [5, 12], 'radius': 5, 'speed': -2, 'until_step': 5},
                  {'goto': [0, 2, 0], 'heading': 0, 'wait': 10}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- B10
# The drone starts facing the pilot, so the frame's +Z points at the pilot and "away" is -Z.
steps = [
    {'say': t('Take off and back away into the box; hold it for 15 seconds, nose towards you', 'Ұшып көтеріліп, шегініп қорапқа кіріңіз; тұмсықты өзіңізге қаратып, 15 секунд ұстаңыз', 'Взлетите и отойдите назад в куб; удерживайте 15 секунд носом к себе'),
     'zone': zone(0, 2, -4), 'for': 15},
    {'say': t('Come back over the pad', 'Алаңшаның үстіне қайтыңыз', 'Вернитесь и зависните над площадкой'), 'zone': zone(0, 2, 0), 'for': 2},
    {'say': t('Land on the pad, nose still towards you', 'Тұмсықты өзіңізге қаратқан күйі алаңшаға қоныңыз', 'Сядьте на площадку, по-прежнему носом к себе'), 'landed': True, 'pad': HOME},
]
EXERCISES.append({
    'format': 1, 'id': 'b10-nose-in', 'type': 'exercise',
    'title': t('Nose-in hover and landing', 'Тұмсықты өзіне қаратып қалықтау және қону', 'Висение и посадка носом к себе'),
    'briefing': t('The drone faces you. Now its left is your right, and pushing the stick forward brings it towards you. Hover in the box and land without turning it round.',
                  'Дрон сізге қарап тұр. Енді оның сол жағы сіздің оң жағыңыз, ал стикті алға итерсеңіз, ол сізге жақындайды. Қорапта қалықтап, дронды бұрмай қоныңыз.',
                  'Дрон смотрит на вас. Теперь его лево для вас справа, а стик вперёд ведёт его к вам. Зависните в кубе и сядьте, не разворачивая дрон.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(LOS, start={'x': 0, 'z': 0, 'heading': 180}, objects=[PAD, box('away', 0, 2, -4)]),
    'rubric': [steps_line(steps, 40),
               {'id': 'heading', 'metric': 'heading_error', 'label': t('Keeping the nose towards you', 'Тұмсықты өзіңізге қаратып ұстау', 'Удержание носом к себе'),
                'tip': t('Do not turn the drone to make it easier: small stick moves, and think from the drone\'s seat.', 'Жеңілдету үшін дронды бұрмаңыз: стикті аз-аздан қозғап, дронның орнынан ойлаңыз.', 'Не разворачивайте дрон, чтобы стало проще: маленькие движения стиком, думайте с места дрона.'),
                'weight': 25, 'best': 5, 'worst': 40, 'params': {'heading': 0}},
               height_line(10, 2, 0.2, 1.0, **{'from': 6}), landing_line(15), smooth_line(10)],
    'reference': [{'takeoff': 2, 'wait': 5}, {'goto': [0, 2, -4], 'heading': 0, 'until_step': 1}, {'goto': [0, 2, 0], 'heading': 0, 'until_step': 2},
                  {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- B11
pads = [(-5, 5, 2.0), (5, 5, 1.5), (5, 12, 1.0), (-5, 12, 0.7)]
steps, ref = [], []
for i, (x, z, size) in enumerate(pads):
    steps.append({'say': t('Land on pad %d and switch the motors off' % (i + 1), '%d-алаңшаға қонып, қозғалтқыштарды өшіріңіз' % (i + 1), 'Сядьте на площадку %d и выключите моторы' % (i + 1)),
                  'landed': True, 'pad': {'x': x, 'z': z, 'radius': size / 2}})
    ref += [{'takeoff': 2, 'wait': 4}, {'goto': [x, 2, z], 'heading': 0, 'wait': 9}, {'land': True, 'until_step': i + 1}]
steps.append(LAND_HOME)
ref += [{'takeoff': 2, 'wait': 4}, {'goto': [0, 2, 0], 'heading': 0, 'wait': 10}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'b11-landing-circuit', 'type': 'exercise',
    'title': t('Landing circuit', 'Қону айналымы', 'Круг посадок'),
    'briefing': t('Four pads, each smaller than the one before. Land on each in turn and switch the motors off, then take off again for the next. Finish on the pad you started from.',
                  'Төрт алаңша, әрқайсысы алдыңғысынан кіші. Әрқайсысына кезекпен қонып, қозғалтқыштарды өшіріңіз, содан кейін келесісі үшін қайта көтеріліңіз. Бастаған алаңшада аяқтаңыз.',
                  'Четыре площадки, каждая меньше предыдущей. Сядьте на каждую по очереди и выключите моторы, затем взлетайте к следующей. Закончите на площадке, с которой начали.'),
    'pass': 60, 'time_limit': 300,
    'setup': dict(LOS, end={'landing': False}, objects=[PAD] + [{'type': 'pad', 'id': 'p%d' % (i + 1), 'x': x, 'y': 0, 'z': z, 'size': size} for i, (x, z, size) in enumerate(pads)]),
    'rubric': [steps_line(steps, 60),
               {'id': 'touch', 'metric': 'touchdown_speed', 'label': t('A soft landing', 'Жұмсақ қону', 'Мягкая посадка'),
                'tip': t('Slow the descent in the last metre.', 'Соңғы метрде төмендеуді баяулатыңыз.', 'Замедлите снижение на последнем метре.'),
                'weight': 15, 'best': 0.5, 'worst': 2.5},
               time_line(15, 150, 290), smooth_line(10)],
    'reference': ref,
})

# ---------------------------------------------------------------- B12
steps = [{'say': t('Take off and hover over the pad for 10 seconds; hold against the wind', 'Ұшып көтеріліп, алаңша үстінде 10 секунд қалықтаңыз; желге қарсы ұстаңыз', 'Взлетите и зависните над площадкой на 10 секунд; держитесь против ветра'),
          'zone': zone(0, 2, 0, 1.2), 'for': 10}]
ref = [{'takeoff': 2, 'until_step': 1}]
for i in range(5):
    x, z = sq[i % 4]
    n = i % 4 + 1
    steps.append({'say': t('Fly to corner %d and stop' % n if i < 4 else 'Close the square: fly back to corner 1',
                           '%d-бұрышқа ұшып, тоқтаңыз' % n if i < 4 else 'Шаршыны жабыңыз: 1-бұрышқа қайта ұшыңыз',
                           'Летите к углу %d и остановитесь' % n if i < 4 else 'Замкните квадрат: вернитесь к углу 1'),
                  'zone': zone(x, 2, z, 1.2), 'for': 1})
    ref.append({'goto': [x, 2, z], 'heading': 0, 'until_step': len(steps)})
steps.append(LAND_HOME)
ref += [{'goto': [0, 2, 0], 'heading': 0, 'wait': 8}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'b12-no-position-hold', 'type': 'exercise',
    'title': t('Without position hold, in wind', 'Орынды ұстаусыз, желде', 'Без удержания позиции, на ветру'),
    'briefing': t('Position hold is switched off: the drone keeps its height but no longer stays where you leave it, and a light wind pushes it sideways. Hover, then fly the square. This is how a drone flies when it loses its satellite fix.',
                  'Орынды ұстау өшірілген: дрон биіктігін сақтайды, бірақ қалдырған жеріңізде тұрмайды, әрі жеңіл жел оны бүйірге итереді. Қалықтаңыз, содан кейін шаршыны ұшыңыз. Спутниктік сигналын жоғалтқан дрон осылай ұшады.',
                  'Удержание позиции выключено: дрон держит высоту, но больше не стоит там, где вы его оставили, и лёгкий ветер сносит его вбок. Зависните, затем пролетите квадрат. Так летит дрон, потерявший спутники.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(LOS, assist='altitude', weather={'wind': 3, 'wind_from': 90, 'gusts': 0.2},
                  objects=[PAD, box('hover', 0, 2, 0, 2.4, 1)] + [box('c%d' % (i + 1), x, 2, z, 2.4, 1) for i, (x, z) in enumerate(sq)]),
    'rubric': [steps_line(steps, 55), landing_line(20), smooth_line(10), time_line(15, 100, 220)],
    'reference': ref,
})


# ================================================================ Block C: camera-drone proficiency
# Modelled on the open-lane tests for small drones: the camera must look straight into each bucket. Flown through the
# drone's own stabilised camera.
CAM = dict(CAMERA, view='fpv')


def bucket(id, x, y, z, heading=180, tilt=0):
    return {'type': 'target', 'id': id, 'x': x, 'y': y, 'z': z, 'size': 0.4, 'heading': heading, 'tilt': tilt}


def shot(x, y, z, heading=180, tilt=0, **more):
    return dict({'x': x, 'y': y, 'z': z, 'heading': heading, 'tilt': tilt}, **more)


def photos_line(weight, needed):
    return {'id': 'photos', 'metric': 'event_count', 'label': t('Photos taken', 'Түсірілген фотолар', 'Сделано снимков'),
            'tip': t('Line the camera up first, then take one photo: a photo that misses the bucket is wasted.', 'Алдымен камераны дәлдеңіз, содан кейін бір фото түсіріңіз: шелекке түспеген фото босқа кетеді.',
                     'Сначала наведите камеру, потом сделайте один снимок: снимок мимо ведра потрачен зря.'),
            'weight': weight, 'best': needed, 'worst': needed * 3, 'params': {'type': 'photo'}}


AIM = t('Look straight into the bucket: the camera must be in front of its mouth, not to one side.', 'Шелектің ішіне тура қараңыз: камера оның аузының жанында емес, дәл алдында болуы керек.',
        'Смотрите прямо в ведро: камера должна быть напротив его горловины, а не сбоку.')
KEYS = t('Page Up and Page Down tilt the camera, Z and X zoom, P takes a photo.', 'Page Up және Page Down камераны еңкейтеді, Z және X үлкейтеді, P фото түсіреді.',
         'Page Up и Page Down наклоняют камеру, Z и X меняют приближение, P делает снимок.')

# ---------------------------------------------------------------- C13
steps = [
    {'say': t('Photograph the low bucket from straight in front', 'Төменгі шелекті дәл алдынан суретке түсіріңіз', 'Сфотографируйте нижнее ведро точно спереди'), 'photo': shot(0, 1, 10)},
    {'say': t('Climb and photograph the high bucket', 'Көтеріліп, жоғарғы шелекті суретке түсіріңіз', 'Поднимитесь и сфотографируйте верхнее ведро'), 'photo': shot(0, 3, 10)},
    {'say': t('Fly over the bucket on the ground, tilt the camera straight down and photograph it', 'Жердегі шелектің үстіне ұшып, камераны тік төмен еңкейтіп, суретке түсіріңіз', 'Зависните над ведром на земле, наклоните камеру строго вниз и сфотографируйте его'),
     'photo': shot(0, 0.3, 5, 0, 90)},
    LAND_HOME,
]
EXERCISES.append({
    'format': 1, 'id': 'c13-position', 'type': 'exercise',
    'title': t('Position: three buckets', 'Орналасу: үш шелек', 'Позиция: три ведра'),
    'briefing': t('Three buckets: one low, one high, one on the ground with its mouth up. Put the drone where the camera looks straight into each one and take a photo. ' + KEYS,
                  'Үш шелек: бірі төмен, бірі жоғары, бірі жерде аузы жоғары қарап жатыр. Камера әрқайсысының ішіне тура қарайтындай дронды орналастырып, фото түсіріңіз. ' + TEXTS[KEYS][0],
                  'Три ведра: одно низко, одно высоко, одно лежит на земле горловиной вверх. Поставьте дрон так, чтобы камера смотрела прямо в каждое, и сделайте снимок. ' + TEXTS[KEYS][1]),
    'pass': 60, 'time_limit': 240,
    'setup': dict(CAM, objects=[PAD, bucket('low', 0, 1, 10), bucket('high', 0, 3, 10), bucket('ground', 0, 0.3, 5, 0, 90)]),
    'rubric': [dict(steps_line(steps, 60), tip=AIM), photos_line(10, 3), landing_line(15), time_line(15, 70, 200)],
    'reference': [{'takeoff': 1, 'wait': 4}, {'goto': [0, 1, 5], 'heading': 0, 'wait': 6}, {'gimbal': 0, 'photo': True, 'until_step': 1},
                  {'goto': [0, 3, 5], 'heading': 0, 'wait': 6}, {'photo': True, 'until_step': 2},
                  {'goto': [0, 4.5, 4.9], 'heading': 0, 'wait': 6}, {'gimbal': -90, 'wait': 1.5}, {'photo': True, 'until_step': 3},
                  {'gimbal': 0, 'wait': 0.5}, {'goto': [0, 2, 0], 'heading': 0, 'wait': 7}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- C14
row = [-6, -2, 2, 6]
steps = [{'say': t('Photograph bucket %d from straight in front' % (i + 1), '%d-шелекті дәл алдынан суретке түсіріңіз' % (i + 1), 'Сфотографируйте ведро %d точно спереди' % (i + 1)), 'photo': shot(x, 2, 10)}
         for i, x in enumerate(row)] + [LAND_HOME]
ref = [{'takeoff': 2, 'wait': 4}]
for i, x in enumerate(row):
    ref += [{'goto': [x, 2, 5], 'heading': 0, 'wait': 7}, {'gimbal': 0, 'photo': True, 'until_step': i + 1}]
ref += [{'goto': [0, 2, 0], 'heading': 0, 'wait': 9}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'c14-traverse', 'type': 'exercise',
    'title': t('Traverse: four buckets in a row', 'Траверс: қатардағы төрт шелек', 'Траверс: четыре ведра в ряд'),
    'briefing': t('Four buckets stand in a row, all at 2 m. Slide sideways along the row, level and at the same distance, and photograph each one from straight in front. Do not turn the drone.',
                  'Төрт шелек бір қатарда, барлығы 2 м биіктікте тұр. Қатар бойымен бір деңгейде әрі бірдей қашықтықта бүйірлеп жылжып, әрқайсысын дәл алдынан суретке түсіріңіз. Дронды бұрмаңыз.',
                  'Четыре ведра стоят в ряд, все на высоте 2 м. Смещайтесь вбок вдоль ряда, на одной высоте и на одном расстоянии, и снимайте каждое точно спереди. Дрон не поворачивайте.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(CAM, objects=[PAD] + [bucket('b%d' % (i + 1), x, 2, 10) for i, x in enumerate(row)]),
    'rubric': [dict(steps_line(steps, 50), tip=AIM), height_line(15, 2, 0.2, 1.0, **{'from': 6}), photos_line(10, 4), landing_line(10), time_line(15, 80, 220)],
    'reference': ref,
})

# ---------------------------------------------------------------- C15
hub = {'x': 0, 'z': 12, 'near': 2.5, 'far': 7.5}
sides = [(0, 11.6, 180, 0, 7, 0), (-0.4, 12, 270, -5, 12, 90), (0, 12.4, 0, 0, 17, 180), (0.4, 12, 90, 5, 12, 270)]  # bucket x, z, facing; drone x, z, heading
steps = [{'say': t('Photograph the bucket facing you', 'Сізге қараған шелекті суретке түсіріңіз', 'Сфотографируйте ведро, обращённое к вам') if i == 0 else
                 t('Go on round the stand and photograph the next bucket', 'Тіректі айнала жалғастырып, келесі шелекті суретке түсіріңіз', 'Продолжайте облёт стойки и сфотографируйте следующее ведро'),
          'photo': shot(bx, 2, bz, facing)} for i, (bx, bz, facing, _, _, _) in enumerate(sides)] + [LAND_HOME]
ref = [{'takeoff': 2, 'wait': 4}, {'goto': [0, 2, 7], 'heading': 0, 'wait': 7}, {'gimbal': 0, 'photo': True, 'until_step': 1}]
for i, (_, _, _, dx, dz, heading) in enumerate(sides[1:]):
    ref += [{'orbit': [0, 12], 'radius': 5, 'speed': 2, 'wait': 4.2}, {'goto': [dx, 2, dz], 'heading': heading, 'wait': 5}, {'photo': True, 'until_step': i + 2}]
ref += [{'orbit': [0, 12], 'radius': 5, 'speed': 2, 'wait': 4.2}, {'goto': [0, 2, 7], 'heading': 0, 'wait': 4}, {'goto': [0, 2, 0], 'heading': 0, 'wait': 8}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'c15-orbit', 'type': 'exercise',
    'title': t('Orbit: four sides of a stand', 'Орбита: тіректің төрт жағы', 'Орбита: четыре стороны стойки'),
    'briefing': t('A stand carries four buckets, one facing each way. Circle it clockwise, 5 m out, with the camera on the stand, and photograph each bucket as you come in front of it.',
                  'Тіректе төрт шелек бар, әрқайсысы бір жаққа қарап тұр. Камераны тірекке қаратып, оны 5 м қашықтықта сағат тілі бағытымен айналыңыз да, әр шелектің алдына келгенде суретке түсіріңіз.',
                  'На стойке четыре ведра, по одному на каждую сторону. Облетайте её по часовой стрелке в 5 м, держа камеру на стойке, и снимайте каждое ведро, когда окажетесь напротив.'),
    'pass': 60, 'time_limit': 300,
    'setup': dict(CAM, objects=[PAD, pole('stand', 0, 12, 1.8)] + [bucket('s%d' % (i + 1), bx, 2, bz, facing) for i, (bx, bz, facing, _, _, _) in enumerate(sides)]),
    'rubric': [dict(steps_line(steps, 45), tip=AIM),
               {'id': 'radius', 'metric': 'radius_error', 'label': t('Keeping the distance from the stand', 'Тіректен қашықтықты сақтау', 'Удержание расстояния до стойки'),
                'tip': t('If the circle grows, push a little towards the stand; if it shrinks, pull away.', 'Шеңбер үлкейсе, тірекке сәл жақындаңыз; кішірейсе, алыстаңыз.', 'Если круг растёт, чуть подайте дрон к стойке; если сжимается, отойдите.'),
                'weight': 15, 'best': 0.4, 'worst': 2, 'params': dict(hub, radius=5)},
               {'id': 'aim', 'metric': 'aim_error', 'label': t('Camera on the stand', 'Камера тірекке қараған', 'Камера на стойке'),
                'tip': t('Turn with the left stick at the same pace as you slide with the right one.', 'Оң стикпен жылжыған қарқынмен сол стикпен бұрылыңыз.', 'Поворачивайте левым стиком в том же темпе, в каком смещаетесь правым.'),
                'weight': 15, 'best': 8, 'worst': 40, 'params': hub},
               height_line(10, 2, 0.2, 1.0, **{'from': 6}), landing_line(15)],
    'reference': ref,
})

# ---------------------------------------------------------------- C16
tower = [(0, 2, 11.6, 180, 0, 7, 0), (-0.4, 4, 12, 270, -5, 12, 90), (0, 6, 12.4, 0, 0, 17, 180), (0.4, 4, 12, 90, 5, 12, 270)]  # bucket x, y, z, facing; drone x, z, heading
steps = [{'say': t('Photograph the bucket at %d m' % by, '%d м биіктіктегі шелекті суретке түсіріңіз' % by, 'Сфотографируйте ведро на высоте %d м' % by), 'photo': shot(bx, by, bz, facing)}
         for (bx, by, bz, facing, _, _, _) in tower[:3]]
steps.append({'say': t('Come down the other side and photograph the last bucket, at 4 m', 'Екінші жағынан төмендеп, 4 м биіктіктегі соңғы шелекті суретке түсіріңіз', 'Снизьтесь с другой стороны и сфотографируйте последнее ведро на 4 м'),
              'photo': shot(*tower[3][:4])})
steps.append(LAND_HOME)
ref = [{'takeoff': 2, 'wait': 4}]
for i, (_, by, _, _, dx, dz, heading) in enumerate(tower):
    ref += [{'goto': [dx, by, dz], 'heading': heading, 'wait': 9}, {'gimbal': 0, 'photo': True, 'until_step': i + 1}]
ref += [{'goto': [0, 2, 7], 'heading': 0, 'wait': 9}, {'goto': [0, 2, 0], 'heading': 0, 'wait': 8}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'c16-spiral', 'type': 'exercise',
    'title': t('Spiral: up and down a tower', 'Спираль: мұнара бойымен жоғары-төмен', 'Спираль: вверх и вниз вокруг вышки'),
    'briefing': t('The buckets are on a tower, at 2 m, 4 m, 6 m and 4 m, each facing a different way. Climb round the tower to the top and come down the other side, photographing each bucket from in front.',
                  'Шелектер мұнарада 2 м, 4 м, 6 м және 4 м биіктікте тұр, әрқайсысы әр жаққа қараған. Мұнараны айнала ұшар басына дейін көтеріліп, екінші жағынан төмендеңіз де, әр шелекті алдынан суретке түсіріңіз.',
                  'Вёдра закреплены на вышке на высоте 2 м, 4 м, 6 м и 4 м, каждое смотрит в свою сторону. Поднимитесь вокруг вышки до верха и спуститесь с другой стороны, снимая каждое ведро спереди.'),
    'pass': 60, 'time_limit': 300,
    'setup': dict(CAM, objects=[PAD, pole('tower', 0, 12, 6.4)] + [bucket('t%d' % (i + 1), bx, by, bz, facing) for i, (bx, by, bz, facing, _, _, _) in enumerate(tower)]),
    'rubric': [dict(steps_line(steps, 55), tip=AIM),
               {'id': 'aim', 'metric': 'aim_error', 'label': t('Camera on the tower', 'Камера мұнараға қараған', 'Камера на вышке'),
                'tip': t('Climb and turn at the same time, a little of each: the tower should stay in the middle of the picture.', 'Бір мезгілде аз-аздан көтеріліп әрі бұрылыңыз: мұнара суреттің ортасында қалуы керек.',
                         'Набирайте высоту и поворачивайте одновременно, понемногу: вышка должна оставаться в центре кадра.'),
                'weight': 15, 'best': 8, 'worst': 40, 'params': hub},
               photos_line(10, 4), landing_line(10), time_line(10, 100, 260)],
    'reference': ref,
})

# ---------------------------------------------------------------- C17
ground = [(-6, 8), (0, 14), (6, 8)]
steps = [{'say': t('Fly over bucket %d, look straight down and photograph it' % (i + 1), '%d-шелектің үстіне ұшып, тік төмен қарап, суретке түсіріңіз' % (i + 1), 'Зависните над ведром %d, посмотрите строго вниз и сфотографируйте его' % (i + 1)),
          'photo': shot(x, 0.3, z, 0, 90, near=2.5, far=12, cone=12)} for i, (x, z) in enumerate(ground)] + [LAND_HOME]
ref = [{'takeoff': 3, 'wait': 4}, {'gimbal': -90, 'wait': 0.5}]
for i, (x, z) in enumerate(ground):
    ref += [{'goto': [x, 5, z - 0.1], 'heading': 0, 'wait': 9}, {'photo': True, 'until_step': i + 1}]
ref += [{'gimbal': 0, 'wait': 0.5}, {'goto': [0, 2, 0], 'heading': 0, 'wait': 10}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'c17-recon', 'type': 'exercise',
    'title': t('Recon: look into three buckets', 'Барлау: үш шелектің ішіне қарау', 'Разведка: заглянуть в три ведра'),
    'briefing': t('Three buckets lie on the ground with their mouths up. Fly over each at 3 m or more, tilt the camera straight down, bring the bucket to the middle of the picture and photograph it. Zoom in if you need to.',
                  'Үш шелек жерде аузы жоғары қарап жатыр. Әрқайсысының үстінен 3 м немесе одан биік ұшып, камераны тік төмен еңкейтіңіз, шелекті суреттің ортасына келтіріп, суретке түсіріңіз. Қажет болса, үлкейтіңіз.',
                  'Три ведра лежат на земле горловиной вверх. Пролетите над каждым на высоте 3 м или выше, наклоните камеру строго вниз, выведите ведро в центр кадра и сделайте снимок. При необходимости приблизьте.'),
    'pass': 60, 'time_limit': 300,
    'setup': dict(CAM, objects=[PAD] + [bucket('g%d' % (i + 1), x, 0.3, z, 0, 90) for i, (x, z) in enumerate(ground)]),
    'rubric': [dict(steps_line(steps, 60), tip=AIM), photos_line(10, 3), landing_line(15), time_line(15, 80, 240)],
    'reference': ref,
})

# ---------------------------------------------------------------- C18
steps = [
    {'say': t('Fly out to the far box and hold it for 3 seconds', 'Алыс қорапқа ұшып, оны 3 секунд ұстаңыз', 'Летите к дальнему кубу и удерживайте его 3 секунды'), 'zone': zone(0, 3, 20), 'for': 3},
    {'say': t('The battery is low: fly straight back and stop over the pad', 'Батарея таусылып барады: тура кері ұшып, алаңша үстінде тоқтаңыз', 'Батарея садится: летите прямо назад и остановитесь над площадкой'), 'zone': zone(0, 3, 0, 1.5), 'for': 2},
    {'say': t('A motor is weak: land now, gently, on the pad', 'Бір қозғалтқыш әлсіреді: қазір алаңшаға жайлап қоныңыз', 'Один мотор ослаб: садитесь сейчас, мягко, на площадку'), 'landed': True, 'pad': {'x': 0, 'z': 0, 'radius': 1.5}},
]
EXERCISES.append({
    'format': 1, 'id': 'c18-emergency', 'type': 'exercise',
    'title': t('Emergency drills', 'Төтенше жағдай жаттығулары', 'Аварийные ситуации'),
    'briefing': t('Things go wrong on this flight. When you reach the far box the battery drops: come straight home. Over the pad a motor weakens: land at once and gently. Read the top of the screen and do not hurry more than you must.',
                  'Бұл ұшуда ақаулар болады. Алыс қорапқа жеткенде батарея күрт азаяды: бірден кері қайтыңыз. Алаңша үстінде бір қозғалтқыш әлсірейді: дереу әрі жайлап қоныңыз. Экранның жоғарғы жағын оқып, қажеттен артық асықпаңыз.',
                  'В этом полёте случаются отказы. Когда долетите до дальнего куба, просядет батарея: сразу возвращайтесь. Над площадкой ослабнет мотор: садитесь немедленно и мягко. Читайте подсказку вверху экрана и не спешите больше, чем нужно.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(CAMERA, objects=[PAD, box('far', 0, 3, 20), box('over', 0, 3, 0, 3, 1)],
                  timeline=[{'after_step': 1, 'name': 'battery_drop', 'value': 0.72}, {'after_step': 2, 'name': 'motor2', 'value': 0.3}]),
    'rubric': [steps_line(steps, 50), dict(landing_line(20), worst=2.0),
               {'id': 'touch', 'metric': 'touchdown_speed', 'label': t('A soft landing', 'Жұмсақ қону', 'Мягкая посадка'),
                'tip': t('With a weak motor the drone sinks faster: add throttle earlier than usual.', 'Қозғалтқыш әлсіз болғанда дрон тезірек төмендейді: газды әдеттегіден ертерек қосыңыз.', 'Со слабым мотором дрон снижается быстрее: добавляйте газ раньше обычного.'),
                'weight': 15, 'best': 0.6, 'worst': 2.5},
               time_line(15, 70, 200)],
    'reference': [{'takeoff': 3, 'wait': 5}, {'goto': [0, 3, 20], 'heading': 0, 'until_step': 1}, {'goto': [0, 3, 0], 'heading': 0, 'until_step': 2},
                  {'land': True, 'until_landed': True}],
})


# ================================================================ Block D: FPV basics
# Flown from the drone's own camera on a 5" quad, with no position or altitude hold: the pilot holds the height with
# the throttle. The flight controller's mode is fixed by the exercise.
FPV = {'place': 'field', 'drone': 'freestyle5', 'assist': 'off', 'view': 'fpv'}


def gate(id, x, y, z, width=2.5, height=2, heading=0):
    return {'type': 'gate', 'id': id, 'x': x, 'y': y, 'z': z, 'size': width, 'height': height, 'heading': heading}


def through(x, y, z, width=2.5, height=2, heading=0):
    return {'x': x, 'y': y, 'z': z, 'width': width, 'height': height, 'heading': heading}


def band_line(weight, low, high, zmin, zmax, best=97, worst=60):
    return {'id': 'band', 'metric': 'height_in_band', 'label': t('Time at the right height', 'Дұрыс биіктіктегі уақыт', 'Время на нужной высоте'),
            'tip': t('The throttle holds the height: tiny moves, and look far ahead, not at the ground under you.', 'Биіктікті газ ұстайды: аз-аздан қозғап, астыңыздағы жерге емес, алысқа қараңыз.',
                     'Высоту держит газ: совсем малые движения, и смотрите далеко вперёд, а не под себя.'),
            'weight': weight, 'best': best, 'worst': worst, 'params': {'low': low, 'high': high, 'zmin': zmin, 'zmax': zmax}}


def far_pad(z):
    return {'type': 'pad', 'id': 'finish', 'x': 0, 'y': 0, 'z': z, 'size': 3}


def land_far(z):
    return {'say': t('Land on the far pad and switch the motors off', 'Алыс алаңшаға қонып, қозғалтқыштарды өшіріңіз', 'Сядьте на дальнюю площадку и выключите моторы'),
            'landed': True, 'pad': {'x': 0, 'z': z, 'radius': 1.5}}


# ---------------------------------------------------------------- D19
zs = [12, 22, 32]
steps = [{'say': t('Take off and hover in the box for 3 seconds', 'Ұшып көтеріліп, қорапта 3 секунд қалықтаңыз', 'Взлетите и зависните в кубе на 3 секунды'), 'zone': zone(0, 2, 4, 1.2), 'for': 3}]
steps += [{'say': t('Fly through gate %d' % (i + 1), '%d-қақпа арқылы ұшып өтіңіз' % (i + 1), 'Пролетите через ворота %d' % (i + 1)), 'gate': through(0, 1, z)} for i, z in enumerate(zs)]
steps.append(land_far(40))
EXERCISES.append({
    'format': 1, 'id': 'd19-three-gates', 'type': 'exercise',
    'title': t('Hover, then three gates in a line', 'Қалықтау, содан кейін бір қатардағы үш қақпа', 'Висение, затем трое ворот по прямой'),
    'briefing': t('Your first flight through the drone\'s own camera. The drone levels itself, but nothing holds its height or its place: that is your job now. Hover in the box, then fly straight through the three gates and land on the far pad.',
                  'Дронның өз камерасы арқылы алғашқы ұшуыңыз. Дрон өзін түзулейді, бірақ биіктігі мен орнын ештеңе ұстамайды: енді бұл сіздің жұмысыңыз. Қорапта қалықтап, үш қақпа арқылы тура ұшып өтіп, алыс алаңшаға қоныңыз.',
                  'Ваш первый полёт через камеру самого дрона. Дрон выравнивается сам, но высоту и место уже ничто не держит: теперь это ваша работа. Зависните в кубе, затем пролетите прямо через трое ворот и сядьте на дальнюю площадку.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(FPV, mode='angle', objects=[PAD, box('hover', 0, 2, 4, 2.4, 1.2), far_pad(40)] + [gate('g%d' % (i + 1), 0, 1, z) for i, z in enumerate(zs)]),
    'rubric': [steps_line(steps, 55), band_line(15, 1.2, 2.8, 8, 34), path_line(15, [[0, 2, 0], [0, 2, 40]], 0.5, 2.0), dict(landing_line(15, 0, 40), worst=2.0)],
    'reference': [{'takeoff': 2, 'wait': 4}, {'goto': [0, 2, 4], 'heading': 0, 'until_step': 1}, {'goto': [0, 2, 36], 'heading': 0, 'speed': 4, 'until_step': 4},
                  {'goto': [0, 2, 40], 'heading': 0, 'wait': 5}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- D20
oval = [(0, 4), (5, 10), (5, 30), (0, 36), (-5, 30), (-5, 10)]
steps, ref = [], [{'takeoff': 2, 'wait': 4}]
for lap in range(2):
    for i in range(6):
        x, z = oval[(i + 1) % 6]
        steps.append({'say': t('Lap %d: follow the oval round the two flags' % (lap + 1), '%d-айналым: екі жалауды айнала сопақша бойымен ұшыңыз' % (lap + 1), 'Круг %d: летите по овалу вокруг двух флагов' % (lap + 1)),
                      'zone': zone(x, 2, z, 2.5, 1.0)})
        ref.append({'goto': [x, 2, z], 'face': True, 'speed': 4, 'until_step': len(steps)})
steps.append(LAND_HOME)
ref += [{'goto': [0, 2, 0], 'heading': 0, 'wait': 7}, {'land': True, 'until_landed': True}]
loop = [[x, 2, z] for x, z in oval] + [[0, 2, 4]]
EXERCISES.append({
    'format': 1, 'id': 'd20-oval', 'type': 'exercise',
    'title': t('Oval round two flags', 'Екі жалауды айнала сопақша', 'Овал вокруг двух флагов'),
    'briefing': t('Fly two laps of an oval round the two flags, at one height. Turn the way an aeroplane does: lean into the turn and turn the nose at the same time, so the camera always looks where you are going.',
                  'Екі жалауды айнала бір биіктікте сопақша бойымен екі айналым ұшыңыз. Ұшақ сияқты бұрылыңыз: бұрылысқа қарай еңкейіп, бір мезгілде тұмсықты бұрыңыз, сонда камера әрдайым ұшу бағытына қарайды.',
                  'Пролетите два круга по овалу вокруг двух флагов на одной высоте. Поворачивайте как самолёт: наклоняйтесь в поворот и одновременно доворачивайте нос, чтобы камера всегда смотрела по ходу движения.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(FPV, mode='angle', objects=[PAD, {'type': 'flag', 'id': 'near', 'x': 0, 'y': 0, 'z': 10, 'size': 1, 'height': 3}, {'type': 'flag', 'id': 'far', 'x': 0, 'y': 0, 'z': 30, 'size': 1, 'height': 3}]),
    'rubric': [steps_line(steps, 50), band_line(20, 1.4, 2.6, 3, 100), path_line(15, loop + loop[1:], 0.7, 3.0), landing_line(15)],
    'reference': ref,
})

# ---------------------------------------------------------------- D21
flags = [8, 14, 20, 26, 32, 38]
steps, ref = [], [{'takeoff': 2, 'wait': 4}]
for i, z in enumerate(flags):
    side = 2 if i % 2 == 0 else -2
    steps.append({'say': t('Pass flag %d on its right' % (i + 1), '%d-жалаудың оң жағынан өтіңіз' % (i + 1), 'Обойдите флаг %d справа' % (i + 1)) if side > 0 else
                         t('Pass flag %d on its left' % (i + 1), '%d-жалаудың сол жағынан өтіңіз' % (i + 1), 'Обойдите флаг %d слева' % (i + 1)),
                  'zone': zone(side, 2, z, 1.6, 1.0)})
    ref.append({'goto': [side, 2, z], 'face': True, 'speed': 3, 'until_step': len(steps)})
steps.append(land_far(46))
ref += [{'goto': [0, 2, 46], 'heading': 0, 'wait': 7}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'd21-slalom', 'type': 'exercise',
    'title': t('Slalom', 'Слалом', 'Слалом'),
    'briefing': t('Six flags in a line. Weave through them: the first on its right, the next on its left, and so on, then land on the far pad. Start each turn early; the drone keeps sliding after you level it.',
                  'Бір қатарда алты жалау тұр. Олардың арасымен ирелеңдеп өтіңіз: біріншісінің оң жағынан, келесісінің сол жағынан, осылай жалғастырып, алыс алаңшаға қоныңыз. Әр бұрылысты ертерек бастаңыз; дрон түзулегеннен кейін де сырғи береді.',
                  'Шесть флагов в ряд. Пройдите между ними змейкой: первый справа, следующий слева и так далее, затем сядьте на дальнюю площадку. Начинайте каждый поворот заранее: дрон продолжает скользить и после выравнивания.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(FPV, mode='angle', objects=[PAD, far_pad(46)] + [{'type': 'flag', 'id': 'f%d' % (i + 1), 'x': 0, 'y': 0, 'z': z, 'size': 1, 'height': 3} for i, z in enumerate(flags)]),
    'rubric': [steps_line(steps, 55), time_line(20, 50, 130), band_line(10, 1.2, 2.8, 4, 42), dict(landing_line(15, 0, 46), worst=2.0)],
    'reference': ref,
})

# ---------------------------------------------------------------- D22
lows = [10, 20, 30, 40]
steps = [{'say': t('Stay low: through gate %d' % (i + 1), 'Төмен ұшыңыз: %d-қақпа арқылы' % (i + 1), 'Держитесь низко: через ворота %d' % (i + 1)), 'gate': through(0, 0.4, z, 3, 1.2)} for i, z in enumerate(lows)]
steps.append(land_far(48))
EXERCISES.append({
    'format': 1, 'id': 'd22-low-flight', 'type': 'exercise',
    'title': t('Low flight', 'Төмен биіктікте ұшу', 'Полёт на малой высоте'),
    'briefing': t('Fly the whole course between half a metre and a metre and a half above the ground, through four low gates. The ground comes up fast in this view: hold the height with small moves of the throttle and do not chase it.',
                  'Бүкіл жолды жерден жарты метр мен бір жарым метр аралығында, төрт аласа қақпа арқылы ұшыңыз. Бұл көріністе жер тез жақындайды: биіктікті газды аз-аздан қозғап ұстаңыз, оны қуаламаңыз.',
                  'Пролетите весь маршрут на высоте от полуметра до полутора метров, через четверо низких ворот. В этом виде земля приближается быстро: держите высоту малыми движениями газа и не гонитесь за ней.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(FPV, mode='horizon', objects=[PAD, far_pad(48)] + [gate('g%d' % (i + 1), 0, 0.4, z, 3, 1.2) for i, z in enumerate(lows)]),
    'rubric': [steps_line(steps, 50), band_line(30, 0.5, 1.5, 6, 44, 95, 50), dict(landing_line(10, 0, 48), worst=2.0), time_line(10, 45, 110)],
    'reference': [{'takeoff': 1, 'wait': 4}, {'goto': [0, 1, 44], 'heading': 0, 'speed': 3, 'until_step': 4}, {'goto': [0, 1, 48], 'heading': 0, 'wait': 5},
                  {'land': True, 'until_landed': True}],
})


# ---------------------------------------------------------------- D23
ACRO = dict(FPV, mode='acro')
steps = [
    {'say': t('Take off and hold the drone inside the box for 20 seconds', 'Ұшып көтеріліп, дронды қорап ішінде 20 секунд ұстаңыз', 'Взлетите и удерживайте дрон внутри куба 20 секунд'), 'zone': zone(0, 3, 6, 1.5, 1.5), 'for': 20},
    LAND_HOME,
]
EXERCISES.append({
    'format': 1, 'id': 'd23-acro-hover', 'type': 'exercise',
    'title': t('Acro hover', 'Акро режимінде қалықтау', 'Висение в акро'),
    'briefing': t('Acro mode: the drone no longer levels itself. It stays at whatever angle you leave it, so every lean must be taken out again by hand. Hold it inside the 3 m box for 20 seconds, then land.',
                  'Акро режимі: дрон енді өзін түзулемейді. Ол сіз қалдырған бұрышта қалады, сондықтан әр еңкеюді қолмен қайтару керек. Оны 3 м қораптың ішінде 20 секунд ұстап, содан кейін қоныңыз.',
                  'Режим акро: дрон больше не выравнивается сам. Он остаётся под тем углом, под которым вы его оставили, поэтому каждый наклон нужно убирать вручную. Удержите его в кубе 3 м в течение 20 секунд, затем сядьте.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(ACRO, objects=[PAD, box('hover', 0, 3, 6, 3, 3)]),
    'rubric': [dict(steps_line(steps, 50), worst=0),
               {'id': 'hold', 'metric': 'hold_time', 'label': t('Longest stay inside the box', 'Қорап ішінде ең ұзақ болған уақыт', 'Самое долгое пребывание в кубе'),
                'tip': t('Make each correction half as big as you think it needs, and take it out at once.', 'Әр түзетуді өзіңіз ойлағаннан екі есе аз жасап, оны бірден қайтарыңыз.', 'Делайте каждую поправку вдвое меньше, чем кажется нужным, и сразу убирайте её.'),
                'weight': 20, 'best': 20, 'worst': 3, 'params': {'x': 0, 'z': 6, 'radius': 1.5, 'y': 3, 'height_tolerance': 1.5}},
               landing_line(15), smooth_line(15)],
    'reference': [{'takeoff': 3, 'wait': 4}, {'goto': [0, 3, 6], 'heading': 0, 'until_step': 1}, {'goto': [0, 2, 0], 'heading': 0, 'wait': 7}, {'land': True, 'until_landed': True}],
})


def course(gates, laps=1, speed=4, run=4.0):
    """Steps and a reference for a course of gates given as (x, bottom, z, heading, width, height)."""
    import math
    steps, ref = [], [{'takeoff': 2, 'wait': 4}]
    for lap in range(laps):
        for i, (x, y, z, h, w, hh) in enumerate(gates):
            say = (t('Lap %d, gate %d' % (lap + 1, i + 1), '%d-айналым, %d-қақпа' % (lap + 1, i + 1), 'Круг %d, ворота %d' % (lap + 1, i + 1)) if laps > 1 else
                   t('Gate %d' % (i + 1), '%d-қақпа' % (i + 1), 'Ворота %d' % (i + 1)))
            steps.append({'say': say, 'gate': through(x, y, z, w, hh, h)})
            dx, dz = math.sin(math.radians(h)), math.cos(math.radians(h))
            mid = y + hh / 2
            # Line up in front of the gate, then fly out the other side of it.
            ref.append({'goto': [round(x - dx * run, 2), mid, round(z - dz * run, 2)], 'heading': h, 'speed': speed, 'wait': 0})
            ref[-1]['until_near'] = True
            ref.append({'goto': [round(x + dx * run, 2), mid, round(z + dz * run, 2)], 'heading': h, 'speed': speed, 'until_step': len(steps)})
    return steps, ref


# ---------------------------------------------------------------- D24
diamond = [(-8, 1, 20, 0, 3, 2), (0, 1, 32, 90, 3, 2), (8, 1, 20, 180, 3, 2), (0, 1, 8, 270, 3, 2)]
steps, ref = course(diamond, laps=2)
steps.append(LAND_HOME)
ref += [{'goto': [0, 2, 0], 'heading': 0, 'wait': 9}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'd24-acro-circuit', 'type': 'exercise',
    'title': t('Acro circuit', 'Акро айналымы', 'Круг в акро'),
    'briefing': t('Four gates in a ring, two laps, in acro mode. Fly the ring to the right: west gate, north gate, east gate, south gate. Keep a steady, easy speed: a clean lap matters more than a fast one.',
                  'Шеңбер бойында төрт қақпа, екі айналым, акро режимінде. Шеңберді оңға қарай ұшыңыз: батыс, солтүстік, шығыс, оңтүстік қақпа. Бірқалыпты, жеңіл жылдамдықты ұстаңыз: таза айналым жылдам айналымнан маңыздырақ.',
                  'Четверо ворот по кольцу, два круга, в режиме акро. Летите по кольцу вправо: западные, северные, восточные, южные ворота. Держите ровную, спокойную скорость: чистый круг важнее быстрого.'),
    'pass': 60, 'time_limit': 240,
    'setup': dict(ACRO, objects=[PAD] + [gate('g%d' % (i + 1), x, y, z, w, hh, h) for i, (x, y, z, h, w, hh) in enumerate(diamond)]),
    'rubric': [steps_line(steps, 60), time_line(20, 70, 200), band_line(10, 1.0, 3.2, -100, 100, 95, 55), landing_line(10)],
    'reference': ref,
})

# The right stick pulled fully back (nose up), as the reference flights give it.
PITCH_BACK = -1

# ================================================================ Block E: racing and freestyle technique
RACE = {'place': 'field', 'drone': 'race5', 'assist': 'off', 'view': 'fpv', 'mode': 'acro'}

# ---------------------------------------------------------------- E25
line = [(0, 1, 12, 0, 3, 2), (5, 1, 22, 0, 3, 2), (-5, 1, 32, 0, 3, 2), (0, 1, 44, 90, 3, 2), (8, 1, 34, 180, 3, 2), (8, 1, 14, 180, 3, 2)]
steps, ref = course(line, speed=6)
steps.append({'say': t('Land on the finish pad', 'Мәре алаңшасына қоныңыз', 'Сядьте на финишную площадку'), 'landed': True, 'pad': {'x': 8, 'z': 4, 'radius': 1.5}})
ref += [{'goto': [8, 2, 4], 'heading': 180, 'wait': 7}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'e25-racing-line', 'type': 'exercise',
    'title': t('Racing line: chicane and hairpin', 'Жарыс сызығы: шикана мен шұғыл бұрылыс', 'Гоночная траектория: шикана и шпилька'),
    'briefing': t('Six gates: a chicane, then a hairpin back the way you came. The time decides the stars. Go wide into a turn and close to the inside of it, and look at the gate after the one you are flying through.',
                  'Алты қақпа: шикана, содан кейін келген жаққа қарай шұғыл бұрылыс. Жұлдыздарды уақыт шешеді. Бұрылысқа кеңінен кіріп, оның ішкі жағына жақын өтіңіз, әрі өтіп жатқан қақпадан кейінгі қақпаға қараңыз.',
                  'Шесть ворот: шикана, затем шпилька обратно. Звёзды решает время. Входите в поворот широко и проходите ближе к внутренней стороне, а смотрите на ворота, следующие за теми, через которые летите.'),
    'pass': 60, 'time_limit': 180, 'stars': [60, 78, 92],
    'setup': dict(RACE, objects=[PAD, {'type': 'pad', 'id': 'finish', 'x': 8, 'y': 0, 'z': 4, 'size': 3}] + [gate('g%d' % (i + 1), x, y, z, w, hh, h) for i, (x, y, z, h, w, hh) in enumerate(line)]),
    'rubric': [steps_line(steps, 50),
               {'id': 'time', 'metric': 'duration', 'label': t('Time: gold 40 s, silver 55 s, bronze 75 s', 'Уақыт: алтын 40 с, күміс 55 с, қола 75 с', 'Время: золото 40 с, серебро 55 с, бронза 75 с'),
                'tip': t('Time is lost in the turns: brake less, and start turning before the gate, not in it.', 'Уақыт бұрылыстарда жоғалады: азырақ тежеп, қақпаның ішінде емес, оған жетпей бұрыла бастаңыз.', 'Время теряется в поворотах: тормозите меньше и начинайте поворот до ворот, а не в них.'),
                'weight': 40, 'best': 40, 'worst': 110},
               dict(landing_line(10, 8, 4), worst=2.0)],
    'reference': ref,
})

# Freestyle moves are flown on the sticks in the reference too: the autopilot only carries the drone to the start and
# catches it afterwards. PULL is the right stick pulled fully back.
PULL = PITCH_BACK
HOME_AGAIN = [{'goto': [0, 6, 0], 'heading': 0, 'until_near': True}, {'goto': [0, 2, 0], 'heading': 0, 'until_near': True}, {'goto': [0, 2, 0], 'heading': 0, 'wait': 3},
              {'land': True, 'until_landed': True}]
HEIGHT_TIP = t('Height is your safety: start every new move high, and throttle down while the drone is upside down.',
               'Биіктік - сіздің қауіпсіздігіңіз: әр жаңа қимылды биіктен бастаңыз, ал дрон төңкеріліп тұрғанда газды азайтыңыз.',
               'Высота - ваша безопасность: каждый новый элемент начинайте высоко, а пока дрон перевёрнут, убирайте газ.')


def trick_time(weight, best, worst):
    return dict(time_line(weight, best, worst), tip=HEIGHT_TIP)


# ---------------------------------------------------------------- E26
steps = [
    {'say': t('Climb to 20 m', '20 м биіктікке көтеріліңіз', 'Наберите высоту 20 м'), 'height': 20, 'tolerance': 4},
    {'say': t('Half roll: turn the drone onto its back', 'Жарты бөшке: дронды арқасына аударыңыз', 'Полубочка: переверните дрон на спину'), 'roll': 180},
    {'say': t('Pull the right stick back until the drone is level again', 'Дрон қайта түзелгенше оң стикті өзіңізге тартыңыз', 'Тяните правый стик на себя, пока дрон снова не выровняется'), 'flip': 180},
    {'say': t('Fly level, back the way you came', 'Келген жағыңызға қарай түзу ұшыңыз', 'Летите ровно в обратную сторону'), 'heading': 180, 'tolerance': 35, 'for': 1.5},
    LAND_HOME,
]
EXERCISES.append({
    'format': 1, 'id': 'e26-split-s', 'type': 'exercise',
    'title': t('Split-S', 'Сплит-S', 'Сплит-S'),
    'briefing': t('The split-S turns the drone round and trades height for speed. Climb to 20 m and fly forward. Roll onto your back, lower the throttle, then pull the right stick back: the nose drops through straight down and comes up facing the way you came. The move costs height, so never start it low.',
                  'Сплит-S дронды кері бұрып, биіктікті жылдамдыққа айырбастайды. 20 м-ге көтеріліп, алға ұшыңыз. Арқаңызға аударылып, газды азайтыңыз, содан кейін оң стикті өзіңізге тартыңыз: тұмсық тік төмен арқылы өтіп, келген жағыңызға қарап көтеріледі. Бұл қимыл биіктікті алады, сондықтан оны ешқашан төменнен бастамаңыз.',
                  'Сплит-S разворачивает дрон и меняет высоту на скорость. Наберите 20 м и летите вперёд. Перевернитесь на спину, уберите газ, затем потяните правый стик на себя: нос пройдёт через вертикаль вниз и поднимется уже в обратную сторону. Элемент стоит высоты, поэтому никогда не начинайте его низко.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(RACE, drone='freestyle5', objects=[PAD]),
    'rubric': [steps_line(steps, 70), trick_time(15, 50, 150), landing_line(15)],
    'reference': [{'takeoff': 20, 'until_step': 1}, {'goto': [0, 20, 40], 'heading': 0, 'speed': 5, 'wait': 2.5},
                  {'sticks': {'throttle': 0.3, 'roll': 1}, 'until_step': 2},
                  {'sticks': {'throttle': 0.1, 'pitch': PULL}, 'until_step': 3},
                  {'hold': True, 'heading': 180, 'until_step': 4}] + HOME_AGAIN,
})

# ---------------------------------------------------------------- E27
loop_gate = (0, 1, 20, 0, 3, 2)
GATE_STEP = {'gate': through(0, 1, 20, 3, 2, 0)}
steps = [
    dict(GATE_STEP, say=t('Fly through the gate with good speed', 'Қақпадан жақсы жылдамдықпен ұшып өтіңіз', 'Пролетите ворота на хорошей скорости')),
    {'say': t('Throttle up and pull back until the nose points at the sky', 'Газды қосып, тұмсық аспанға қарағанша стикті тартыңыз', 'Добавьте газ и тяните стик на себя, пока нос не будет смотреть в небо'), 'flip': 90},
    {'say': t('Go over the top of the gate', 'Қақпаның үстінен асып өтіңіз', 'Пройдите над воротами'), 'zone': zone(0, 9, 20, 8, 3.5)},
    {'say': t('Lower the throttle and keep pulling until the drone is level again', 'Газды азайтып, дрон қайта түзелгенше тартуды жалғастырыңыз', 'Уберите газ и продолжайте тянуть, пока дрон снова не выровняется'), 'flip': 240},
    dict(GATE_STEP, say=t('Come through the same gate again', 'Сол қақпадан қайта өтіңіз', 'Пройдите те же ворота ещё раз')),
    land_far(40),
]
EXERCISES.append({
    'format': 1, 'id': 'e27-power-loop', 'type': 'exercise',
    'title': t('Power loop', 'Күш тұзағы', 'Силовая петля'),
    'briefing': t('A power loop goes through a gate, up and over the top of it on its back, and down through the gate again. Come through fast, add throttle and pull back. Over the top, lower the throttle and keep pulling, and look for the gate as the nose comes down. A wide loop is easier than a tight one.',
                  'Күш тұзағында дрон қақпадан өтіп, оның үстінен арқасымен асып, қайтадан қақпадан төмен өтеді. Жылдам өтіп, газды қосып, стикті тартыңыз. Жоғарғы нүктеде газды азайтып, тартуды жалғастырыңыз да, тұмсық төмендегенде қақпаны іздеңіз. Кең тұзақ тар тұзақтан оңайырақ.',
                  'В силовой петле дрон проходит ворота, уходит вверх и через них на спине, и снова проходит ворота сверху вниз. Пройдите быстро, добавьте газ и тяните на себя. В верхней точке уберите газ и продолжайте тянуть, а когда нос пойдёт вниз, ищите ворота взглядом. Широкая петля легче тесной.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(RACE, drone='freestyle5', objects=[PAD, far_pad(40), gate('g1', 0, 1, 20, 3, 2, 0)]),
    'rubric': [steps_line(steps, 70), trick_time(15, 45, 150), dict(landing_line(15, 0, 40), worst=2.0)],
    'reference': [{'takeoff': 2, 'wait': 4}, {'goto': [0, 2, 8], 'heading': 0, 'until_near': True}, {'goto': [0, 2, 30], 'heading': 0, 'speed': 7, 'until_step': 1},
                  {'sticks': {'throttle': 0.8, 'pitch': 0.5 * PULL}, 'until_step': 3},
                  {'sticks': {'throttle': 0.2, 'pitch': 0.5 * PULL}, 'until_step': 4},
                  {'hold': True, 'heading': 0, 'wait': 3}, {'goto': [0, 7, 10], 'heading': 0, 'until_near': True}, {'goto': [0, 2, 12], 'heading': 0, 'until_near': True},
                  {'goto': [0, 2, 28], 'heading': 0, 'speed': 4, 'until_step': 5},
                  {'goto': [0, 2, 40], 'heading': 0, 'until_near': True}, {'goto': [0, 2, 40], 'heading': 0, 'wait': 3}, {'land': True, 'until_landed': True}],
})

# ---------------------------------------------------------------- E28
UP = {'say': t('Climb to 12 m', '12 м биіктікке көтеріліңіз', 'Наберите высоту 12 м'), 'height': 12, 'tolerance': 3}
steps = [
    UP,
    {'say': t('Roll: right stick fully to one side until the drone has gone right round', 'Бөшке: дрон толық айналғанша оң стикті бір жаққа толық итеріңіз', 'Бочка: правый стик до упора в сторону, пока дрон не сделает полный оборот'), 'roll': 360},
    dict(UP, say=t('Level out and climb back to 12 m', 'Түзеліп, қайтадан 12 м-ге көтеріліңіз', 'Выровняйтесь и снова наберите 12 м'), **{'for': 1}),
    {'say': t('Flip: right stick fully forward or back until the drone has gone right over', 'Сальто: дрон толық айналғанша оң стикті алға немесе артқа толық итеріңіз', 'Флип: правый стик до упора вперёд или назад, пока дрон не сделает полный оборот'), 'flip': 360},
    LAND_HOME,
]
EXERCISES.append({
    'format': 1, 'id': 'e28-roll-and-flip', 'type': 'exercise',
    'title': t('Roll and flip', 'Бөшке және сальто', 'Бочка и флип'),
    'briefing': t('The two moves every freestyle trick is built from. Climb to 12 m. For each move: a short burst of throttle, then lower the throttle, push the right stick fully over and hold it until the horizon comes round again, then centre the stick and catch the drone with the throttle.',
                  'Фристайлдың барлық трюктері осы екі қимылдан құралады. 12 м-ге көтеріліңіз. Әр қимыл үшін: газды қысқа уақытқа қосыңыз, содан кейін азайтыңыз, оң стикті толық итеріп, көкжиек қайта оралғанша ұстаңыз, содан соң стикті ортаға қойып, дронды газбен ұстап алыңыз.',
                  'Два элемента, из которых складывается любой трюк фристайла. Наберите 12 м. Для каждого элемента: короткий импульс газа, затем уберите газ, отклоните правый стик до упора и держите, пока горизонт не вернётся, затем верните стик в центр и поймайте дрон газом.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(RACE, drone='freestyle5', objects=[PAD]),
    'rubric': [steps_line(steps, 70), trick_time(15, 45, 150), landing_line(15)],
    'reference': [{'takeoff': 12, 'until_step': 1}, {'goto': [0, 12, 0], 'heading': 0, 'wait': 2},
                  {'sticks': {'throttle': 0.25, 'roll': 1}, 'until_step': 2},
                  {'hold': True, 'heading': 0, 'wait': 2}, {'goto': [0, 12, 0], 'heading': 0, 'until_step': 3}, {'goto': [0, 12, 0], 'heading': 0, 'wait': 2},
                  {'sticks': {'throttle': 0.25, 'pitch': PULL}, 'until_step': 4},
                  {'hold': True, 'heading': 0, 'wait': 2}] + HOME_AGAIN,
})

# ---------------------------------------------------------------- E29
gaps = [(0, 0.5, 10, 0, 3.0, 2.2), (0, 0.7, 18, 0, 2.4, 1.8), (0, 0.9, 26, 0, 1.8, 1.4), (0, 1.0, 34, 0, 1.4, 1.2), (0, 1.1, 42, 0, 1.0, 1.0)]
steps, ref = course(gaps, speed=3, run=3.0)
steps.append(land_far(50))
ref += [{'goto': [0, 2, 50], 'heading': 0, 'wait': 6}, {'land': True, 'until_landed': True}]
EXERCISES.append({
    'format': 1, 'id': 'e29-proximity', 'type': 'exercise',
    'title': t('Proximity: five gaps', 'Жақын ұшу: бес саңылау', 'Пролёты: пять проёмов'),
    'briefing': t('Five openings in a line, each smaller than the one before: from 3 m wide down to 1 m. Fly through the middle of each. Touching a frame hard ends the attempt.',
                  'Бір қатарда бес саңылау, әрқайсысы алдыңғысынан кішірек: ені 3 м-ден 1 м-ге дейін. Әрқайсысының ортасынан ұшып өтіңіз. Жақтауға қатты соғылу әрекетті аяқтайды.',
                  'Пять проёмов по прямой, каждый меньше предыдущего: от 3 м шириной до 1 м. Пролетите через середину каждого. Сильный удар о раму заканчивает попытку.'),
    'pass': 60, 'time_limit': 180,
    'setup': dict(RACE, objects=[PAD, far_pad(50)] + [gate('g%d' % (i + 1), x, y, z, w, hh, h) for i, (x, y, z, h, w, hh) in enumerate(gaps)]),
    'rubric': [dict(steps_line(steps, 70), worst=2), time_line(15, 40, 120), dict(landing_line(15, 0, 50), worst=2.0)],
    'reference': ref,
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
