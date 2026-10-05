# The animated library's second balanced set (wallpaper library 3x, OD-0110 second amendment): sixteen wallpapers,
# colourful but restrained. Each entry is made by wallpaper_kit.wall() (see OPTIONS-FORMAT.md) and its shader is
# walls/<id>.glsl. A role is (dark L, dark C, light L, light C) for one palette slot; a pack is (name, six hues, chroma
# factor, contrast factor, dark hues, extras), made into palettes by catalog.make_pack.
from wallpaper_kit import wall, dyn

WALLS = []


def add(*a, **k):
    WALLS.append(wall(*a, **k))


add('pleat', 'Pleat', 'balanced', 64,
    'Paper folded in a Miura pattern of zigzag pleats; as the sheet gathers and relaxes, the facets tilt through the light.',
     [(.44, .09, .85, .065), (.38, .10, .79, .08), (.72, .10, .97, .045), (.18, .07, .60, .08), (.42, .08, .80, .07), (.84, .05, .99, .02)],
     [('Coral', [35, 20, 70, 15, 250, 80], 1.0, 1.0, [18, 5, 55, 325, 250, 70], {'dl': [.03, .03, .02, 0, 0, 0]}),
      ('Sage', [150, 170, 95, 175, 30, 100]),
      ('Blueprint', [250, 235, 210, 262, 40, 220]),
      ('Lilac', [305, 290, 340, 290, 200, 320]),
      ('Quiet', [255, 245, 80, 250, 40, 80], .45, .9, [270, 280, 75, 265, 230, 80])],
     ('pleats', 'Pleats', 'Wide', 'Fine', .45),
     dynamic=dyn('warm', sun='track'), perspective='layers', focus=0.0,
     match=dict(off=[0, -8, 15, -15, 60, 10], hero=0))

add('isles', 'Isles', 'balanced', 90,
     'Ranks of islands on a still sea at first light, drifting past with their reflections, mist at their feet.',
     [(.22, .06, .80, .06), (.50, .10, .93, .06), (.40, .065, .78, .05), (.21, .06, .52, .065), (.18, .05, .66, .06), (.90, .10, .99, .06)],
     [('First Light', [255, 35, 280, 265, 250, 70], 1.0, 1.0, [260, 15, 290, 270, 255, 55], {'dc': [1, 1.3, 1, 1, 1, 1]}),
      ('Jade', [210, 80, 175, 190, 215, 85], 1.0, 1.0, [220, 10, 185, 195, 220, 60], {'dl': [0, .03, 0, 0, 0, 0]}),
      ('Lavender', [275, 330, 295, 285, 270, 40]),
      ('Monsoon', [235, 210, 225, 230, 230, 200], .55),
      ('Quiet', [250, 40, 260, 255, 245, 70], .45, .85, [255, 350, 265, 258, 250, 50])],
     ('mist', 'Mist', 'Thin', 'Thick', .5),
     dynamic=dyn('sky', sun='track'), perspective='layers', focus=0.0,
     match=dict(off=[0, 45, 12, 6, -6, 20], hero=0))

add('relief', 'Relief', 'balanced', 72,
     'A painted plaster relief of discs and an arch; a low warm light travels round it and the cool shadows swing.',
     [(.36, .045, .86, .04), (.37, .05, .865, .045), (.385, .055, .87, .05), (.34, .055, .83, .05), (.20, .06, .64, .06), (.64, .07, .99, .04)],
     [('Clay', [25, 22, 30, 15, 290, 70], 1.0, 1.0, [355, 350, 0, 345, 285, 60], {'dl': [.02, .02, .02, .02, 0, 0]}),
      ('Sky', [240, 235, 245, 230, 265, 80]),
      ('Sage', [150, 155, 145, 160, 230, 90]),
      ('Lilac', [300, 305, 295, 310, 260, 60]),
      ('Quiet', [255, 255, 250, 250, 260, 75], .45)],
     ('depth', 'Depth', 'Shallow', 'Deep', .5),
     dynamic=dyn('warm', sun='track'), perspective='pan', focus=0.0,
     match=dict(off=[0, -4, 4, -8, 25, 15], hero=0))

add('louver', 'Louver', 'balanced', 80,
     'Late sun through a slatted blind, laid across a quiet wall and folding onto the floor, sliding as the sun moves.',
     [(.27, .04, .80, .022), (.22, .045, .74, .028), (.74, .12, .86, .14), (.60, .15, .78, .15), (.20, .04, .68, .035), (.92, .07, .98, .05)],
     [('Golden Hour', [265, 270, 70, 45, 280, 85], 1.0, 1.0, [268, 278, 72, 35, 285, 85], {'dl': [0, 0, .05, .04, 0, 0]}),
      ('Terracotta', [20, 15, 55, 30, 350, 70], 1.0, 1.0, [330, 325, 45, 20, 320, 60], {'dl': [.02, .02, .06, .04, 0, 0]}),
      ('Sage', [150, 160, 90, 70, 170, 95], 1.0, 1.0, [195, 200, 98, 75, 205, 100], {'dl': [0, 0, .06, .05, 0, 0]}),
      ('Blue Hour', [255, 262, 220, 280, 265, 210], .8),
      ('Quiet', [260, 265, 75, 50, 275, 85], .45, 1.0, [262, 268, 235, 250, 275, 220])],
     ('slats', 'Slats', 'Wide', 'Narrow', .45),
     dynamic=dyn('warm', sun='track'), perspective='pan', focus=0.0,
     match=dict(off=[0, 5, 15, 25, 10, 10], hero=0))

add('kiln', 'Kiln', 'balanced', 90,
    'Close to a tall vase turning under a softbox: one glaze runs down through another and pools in a band.',
    [(.18, .05, .46, .07), (.44, .10, .64, .10), (.50, .13, .70, .12), (.30, .10, .48, .11), (.22, .03, .36, .04), (.96, .02, .99, .01)],
    [('Jun', [262, 248, 310, 255, 260, 260], 1.0, 1.0, None, {'cm': [1, 1, 1.15, 1, 1, 1]}),
     ('Hare\'s Fur', [255, 248, 222, 262, 240, 250]),
     ('Peach Bloom', [330, 350, 15, 320, 300, 30]),
     ('Copper Green', [215, 175, 150, 225, 250, 200], 1.0, 1.0, None, {'dc': [1, .75, .75, 1, 1, 1]}),
     ('Quiet', [230, 200, 180, 225, 250, 220], .45, .9)],
    ('drips', 'Drips', 'Few', 'Many', .5),
    dynamic=dyn('warm', sun='track'), perspective='pan', focus=0.0,
    match=dict(off=[0, -25, 0, 10, 0, 0], hero=2))

add('summit', 'Summit', 'balanced', 60,
    'A snow mountain in the last light: rose on the faces turned to the sun, cold blue in the shade, a plume of spindrift.',
    [(.24, .07, .72, .07), (.50, .09, .90, .06), (.36, .07, .74, .06), (.70, .10, .90, .08), (.20, .04, .45, .05), (.90, .07, .98, .04)],
    [('Rose', [262, 15, 258, 8, 285, 35]),
     ('Ember', [268, 40, 262, 40, 290, 60], 1.0, 1.0, [270, 20, 265, 25, 295, 50], {'dl': [0, .03, 0, .04, 0, 0]}),
     ('Violet', [270, 320, 265, 325, 290, 340]),
     ('Glacier', [245, 210, 240, 200, 255, 190], .7),
     ('Quiet', [262, 20, 258, 15, 285, 40], .45, .9)],
    ('glow', 'Glow', 'Faint', 'Strong', .55),
    dynamic=dyn('sky', sun='track'), perspective='layers', focus=0.0,
    match=dict(off=[0, 60, -6, 60, 20, 70], hero=0))

add('furrow', 'Furrow', 'balanced', 80,
    'A dry garden from above: raked furrows bend into rings round three mossy stones as the low sun moves across.',
    [(.30, .04, .70, .045), (.40, .03, .84, .03), (.52, .04, .94, .035), (.30, .025, .52, .035), (.42, .10, .58, .12), (.64, .10, .95, .08)],
    [('Kyoto', [262, 70, 75, 250, 135, 60], 1.0, 1.0, [262, 250, 70, 250, 140, 55]),
     ('Dusk', [285, 300, 330, 275, 160, 10]),
     ('Moss', [215, 120, 105, 210, 140, 95], 1.0, 1.0, [215, 200, 120, 210, 150, 90]),
     ('Slate', [245, 240, 230, 250, 175, 220], .7),
     ('Quiet', [262, 70, 75, 250, 140, 60], .45, .9, [262, 250, 70, 250, 145, 55])],
    ('furrows', 'Furrows', 'Wide', 'Fine', .45),
    dynamic=dyn('warm', sun='track'), perspective='pan', focus=0.0,
    match=dict(off=[0, 6, 12, -6, -70, 20], hero=0))

add('lucent', 'Lucent', 'balanced', 60,
    'Sunlight through cut glass laid across a table: a band of coloured light with long bright curves that slide and fold.',
    [(.21, .03, .94, .015), (.16, .035, .89, .025), (.55, .13, .78, .13), (.48, .13, .72, .12), (.11, .03, .82, .025), (.95, .04, .99, .03)],
    [('Amber Glass', [250, 255, 75, 25, 260, 85], 1.0, 1.0, [260, 265, 42, 345, 270, 70], {'dl': [0, 0, .08, .06, 0, 0], 'dc': [1, 1, 1.05, 1, 1, 1]}),
     ('Sea Glass', [200, 210, 175, 220, 230, 190]),
     ('Rose', [320, 300, 10, 340, 290, 30]),
     ('Cobalt', [245, 250, 255, 225, 260, 230]),
     ('Quiet', [250, 255, 80, 230, 260, 80], .45, .9, [255, 260, 240, 220, 265, 220])],
    ('caustic', 'Caustic', 'Soft', 'Sharp', .55),
    dynamic=dyn('warm', sun='track'), perspective='pan', focus=0.0,
    match=dict(off=[0, 0, 0, -40, 6, 10], hero=2))

add('rime', 'Rime', 'balanced', 70,
    'Feathered frost grown in from the frame of a window pane, glowing with the low winter sun behind it.',
    [(.26, .06, .58, .09), (.40, .08, .70, .085), (.78, .10, .90, .08), (.55, .03, .98, .01), (.85, .06, 1.0, .02), (.97, .03, 1.0, .0)],
    [('Winter Dawn', [258, 300, 50, 240, 60, 80], 1.0, 1.0, [262, 305, 30, 245, 45, 70]),
     ('Rose', [270, 330, 10, 280, 20, 30]),
     ('Ice', [235, 215, 195, 225, 190, 200], .7),
     ('Lilac', [285, 310, 340, 280, 345, 320]),
     ('Quiet', [258, 290, 55, 245, 60, 80], .45, .9)],
    ('frost', 'Frost', 'Light', 'Heavy', .5),
    dynamic=dyn('sky', sun='track'), perspective='pan', focus=0.0,
    match=dict(off=[0, 35, 70, -10, 70, 0], hero=0))

add('terraces', 'Terraces', 'balanced', 80,
    'Flooded rice terraces from above at sunset: each pool holds its own piece of the sky between curving earthen banks.',
    [(.30, .09, .62, .10), (.48, .10, .78, .08), (.70, .12, .90, .08), (.24, .04, .46, .05), (.36, .06, .58, .075), (.82, .07, .96, .04)],
    [('Sunset', [272, 320, 40, 85, 115, 30], 1.0, 1.0, [272, 325, 20, 80, 120, 15], {'dl': [0, 0, .03, 0, .02, 0]}),
     ('Blue Hour', [255, 275, 310, 110, 135, 290]),
     ('Dawn', [285, 345, 15, 85, 115, 50], 1.0, 1.0, [285, 345, 10, 80, 120, 40]),
     ('Silver', [245, 235, 220, 140, 150, 230], .55),
     ('Quiet', [272, 315, 40, 90, 120, 30], .45, .9, [272, 320, 20, 85, 120, 15])],
    ('terraces', 'Terraces', 'Few', 'Many', .5),
    dynamic=dyn('sky'), perspective='pan', focus=0.0,
    match=dict(off=[0, 40, 75, -60, -50, 80], hero=0))

add('nocturne', 'Nocturne', 'balanced', 90,
    'A long exposure of the night sky over a dark ridge: star trails circle the pole above the last of the twilight.',
    [(.13, .05, .46, .09), (.24, .07, .62, .09), (.46, .10, .80, .08), (.80, .06, .95, .04), (.86, .07, .97, .05), (.08, .02, .26, .04)],
    [('Blue Hour', [268, 262, 300, 245, 75, 270]),
     ('Ember', [275, 300, 40, 235, 80, 285], 1.0, 1.0, [275, 300, 25, 235, 80, 285], {'dl': [0, 0, .04, 0, 0, 0]}),
     ('Aurora', [255, 215, 170, 200, 120, 245]),
     ('Rose', [285, 320, 355, 260, 60, 290]),
     ('Quiet', [262, 262, 290, 245, 75, 265], .45, .9)],
    ('trails', 'Trails', 'Short', 'Long', .55),
    dynamic=dyn('glow', sun='track'), perspective='layers', focus=0.0,
    match=dict(off=[0, -6, 35, -20, 75, 0], hero=0))

add('beacon', 'Beacon', 'balanced', 40,
    'A lighthouse on a dark headland at dusk: its two beams sweep round through the sea haze and flare as they pass.',
    [(.22, .06, .58, .08), (.40, .07, .74, .07), (.20, .05, .52, .07), (.88, .07, .95, .08), (.10, .02, .28, .03), (.98, .05, 1.0, .03)],
    [('Dusk', [262, 300, 250, 75, 270, 80]),
     ('Harbour', [245, 215, 230, 90, 240, 90]),
     ('Rose', [280, 340, 270, 60, 290, 70]),
     ('Storm', [240, 230, 235, 200, 245, 200], .6),
     ('Quiet', [260, 290, 250, 80, 265, 80], .45, .9)],
    ('haze', 'Haze', 'Clear', 'Thick', .5),
    dynamic=dyn('sky'), perspective='layers', focus=0.0,
    match=dict(off=[0, 35, -10, 80, 5, 85], hero=0))

add('reeds', 'Reeds', 'balanced', 36,
    'The edge of a lake at sunrise through a stand of tall reeds, mist on the water and the low sun glowing through it.',
    [(.30, .06, .76, .07), (.56, .07, .90, .05), (.76, .11, .95, .07), (.36, .06, .74, .06), (.14, .03, .36, .05), (.96, .06, .99, .04)],
    [('Sunrise', [262, 300, 55, 255, 290, 75], 1.0, 1.0, [265, 305, 35, 260, 295, 65], {'dl': [0, 0, .04, 0, 0, 0]}),
     ('Marsh', [230, 190, 90, 210, 160, 95], 1.0, 1.0, [235, 200, 70, 215, 175, 80]),
     ('Rose', [280, 330, 15, 270, 300, 40]),
     ('Frost', [240, 225, 210, 235, 245, 200], .6),
     ('Quiet', [262, 295, 60, 255, 285, 75], .45, .9)],
    ('reeds', 'Reeds', 'Sparse', 'Dense', .5),
    dynamic=dyn('sky', sun='track'), perspective='layers', focus=0.0,
    match=dict(off=[0, 40, 75, -6, 30, 80], hero=0))

add('squall', 'Squall', 'balanced', 75,
    'A rain squall far out at sea at sunset: grey veils under a dark shelf of cloud, gold at their edges, mirrored below.',
    [(.26, .05, .66, .07), (.22, .04, .56, .05), (.42, .05, .74, .05), (.72, .11, .92, .08), (.24, .05, .58, .07), (.95, .07, .99, .04)],
    [('Gold', [262, 275, 260, 65, 250, 80], 1.0, 1.0, [265, 280, 265, 45, 255, 70], {'dl': [0, 0, 0, .04, 0, 0]}),
     ('Rose', [270, 290, 280, 15, 260, 40]),
     ('Lagoon', [235, 245, 220, 25, 210, 50]),
     ('Slate', [245, 250, 240, 230, 240, 210], .6),
     ('Quiet', [262, 270, 260, 60, 250, 80], .45, .9, [265, 275, 265, 20, 255, 50])],
    ('rain', 'Rain', 'Light', 'Heavy', .5),
    dynamic=dyn('sky', sun='track'), perspective='layers', focus=0.0,
    match=dict(off=[0, 6, 0, 80, -10, 85], hero=0))

add('glowtide', 'Glowtide', 'balanced', 30,
    'A beach at night with glowing plankton: each breaking wave lights blue-green along its crest and fades on the sand.',
    [(.14, .05, .48, .08), (.30, .07, .68, .07), (.12, .04, .40, .06), (.62, .12, .70, .13), (.18, .03, .50, .04), (.88, .10, .90, .10)],
    [('Plankton', [262, 290, 245, 185, 260, 175]),
     ('Lagoon', [250, 230, 230, 170, 240, 160]),
     ('Violet', [275, 320, 265, 215, 280, 200]),
     ('Moonlit', [250, 240, 245, 220, 250, 210], .6),
     ('Quiet', [262, 285, 245, 190, 255, 180], .45, .9)],
    ('glow', 'Glow', 'Faint', 'Bright', .55),
    dynamic=dyn('glow'), perspective='pan', focus=0.0,
    match=dict(off=[0, 30, -10, -80, 0, -80], hero=0))

add('planish', 'Planish', 'balanced', 60,
    'A sheet of hand-hammered metal, a field of small curved mirrors that shimmer as two studio lights glide across.',
    [(.22, .06, .48, .07), (.40, .08, .66, .08), (.80, .07, .94, .05), (.70, .09, .88, .07), (.14, .04, .34, .05), (.96, .03, .99, .02)],
    [('Copper', [30, 40, 60, 330, 20, 70], 1.0, 1.0, [15, 30, 55, 320, 355, 65], {'dl': [.04, .06, .02, 0, .02, 0]}),
     ('Pewter', [250, 245, 230, 270, 255, 220], .5),
     ('Brass', [70, 80, 90, 40, 50, 95], 1.0, 1.0, [30, 70, 90, 20, 340, 90], {'dl': [.05, .08, .04, .02, .03, 0], 'dc': [1.1, 1.15, 1, 1, 1, 1]}),
     ('Anodised Blue', [255, 245, 220, 290, 262, 200]),
     ('Quiet', [30, 40, 60, 330, 20, 70], .4, .9, [300, 15, 50, 310, 290, 60])],
    ('dishes', 'Dishes', 'Large', 'Small', .45),
    dynamic=dyn('warm', sun='track'), perspective='pan', focus=0.0,
    match=dict(off=[-6, 0, 20, -45, -12, 15], hero=1))
