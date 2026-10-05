# The animated wallpaper library: the catalogue (names, tiers, loop periods, descriptions, options, colour packs) and the
# generator of every pack's dark and light palette. Writes src/wallpapers.json, the data the page runs on and the app
# ports: the shared GLSL (common.glsl), each wallpaper's wall() (walls/<id>.glsl), and its packs and options as plain data.
#
# A pack is written as hues on a wallpaper's own colour roles. Each wallpaper fixes, for its six palette slots, the
# lightness and chroma a slot has in dark and in light; a pack chooses the six hues, a chroma factor (quiet packs are
# low, rich packs high) and a contrast factor (how far the lightnesses spread). Colours are made in OKLCH and brought
# into sRGB by lowering the chroma only, so a hue never shifts on the way into gamut.
import json, math, os, re

HERE = os.path.dirname(os.path.abspath(__file__))


# ---------------------------------------------------------------- OKLCH -> sRGB with gamut mapping by chroma
def lab2lin(L, a, b):
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s)


def enc(c):
    c = min(1.0, max(0.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


def lch_hex(L, C, h):
    L = min(1.0, max(0.0, L))
    lo, hi = 0.0, C
    def ok(c):
        r = lab2lin(L, c * math.cos(math.radians(h)), c * math.sin(math.radians(h)))
        return all(-0.0005 <= v <= 1.0005 for v in r)
    if not ok(C):
        for _ in range(30):
            mid = (lo + hi) / 2
            if ok(mid): lo = mid
            else: hi = mid
        C = lo
    r = lab2lin(L, C * math.cos(math.radians(h)), C * math.sin(math.radians(h)))
    return '#%02x%02x%02x' % tuple(round(enc(v) * 255) for v in r)


def make_pack(roles, name, hues, ck=1.0, lk=1.0, dh=None, x=None):
    # dh: optional hues for the dark palette, for packs whose light hues would turn muddy (brown) when dark.
    # x: optional extras: cm = a chroma multiplier per slot (both themes); dl = a lightness lift per slot for the dark
    # palette (warm colours need more light than cool ones to stay chromatic instead of turning maroon); dc = a chroma
    # multiplier per slot for the dark palette.
    x = x or {}
    cm = x.get('cm', [1.0] * 6)
    out = {'name': name}
    for theme, li, ci in (('dark', 0, 1), ('light', 2, 3)):
        hs = dh if (dh and theme == 'dark') else hues
        Ls = [r[li] for r in roles]
        mid = sum(Ls) / len(Ls)
        dl = x.get('dl', [0.0] * 6) if theme == 'dark' else [0.0] * 6
        dc = x.get('dc', [1.0] * 6) if theme == 'dark' else [1.0] * 6
        cols = []
        for i, r in enumerate(roles):
            L = mid + (r[li] - mid) * lk + dl[i]
            cols.append(lch_hex(L, r[ci] * ck * cm[i] * dc[i], hs[i]))
        out[theme] = cols
    out['id'] = re.sub(r'[^a-z0-9]+', '-', name.lower()).strip('-')
    return out


MOTION = ('motion', 'Motion', 'Calm', 'Lively')
W = []


def wall(id, name, tier, period, desc, roles, packs, opt2, motion=0.5):
    W.append(dict(id=id, name=name, tier=tier, period=period, desc=desc, roles=roles, packs=packs,
                  options=[dict(zip(('id', 'label', 'lo', 'hi'), MOTION), default=motion),
                           dict(zip(('id', 'label', 'lo', 'hi'), opt2[:4]), default=opt2[4])]))


# ================================================================ vibrant
wall('borealis', 'Borealis', 'vibrant', 60,
     'Curtains of aurora over a night sky, rays shimmering up from a bright foot.',
     [(.13, .035, .93, .03), (.21, .06, .97, .03), (.82, .19, .68, .17), (.70, .15, .60, .16), (.56, .21, .52, .2), (.42, .11, .88, .07)],
     [('Polar', [262, 245, 158, 190, 310, 330]),
      ('Solar Wind', [275, 300, 175, 140, 345, 20], 1.15),
      ('Ember Sky', [290, 20, 55, 30, 350, 40], 1.0, 1.0, [285, 305, 55, 30, 340, 15], {'dl': [0, 0, 0, 0, 0, .1], 'dc': [1, 1, 1, 1, 1, 1.2]}),
      ('Glacier', [250, 230, 195, 215, 260, 220]),
      ('Quiet', [262, 250, 170, 200, 290, 300], .5, .75)],
     ('glow', 'Glow', 'Soft', 'Bright', .55))

wall('molten', 'Molten', 'vibrant', 40,
     'Drops of liquid chrome drifting, merging and parting, mirroring a bright studio.',
     [(.13, .02, .9, .012), (.86, .03, .9, .025), (.64, .2, .66, .18), (.22, .05, .34, .05), (.72, .14, .74, .14), (.99, .01, .99, .01)],
     [('Titanium', [265, 245, 320, 255, 200, 90]),
      ('Studio', [255, 250, 45, 240, 0, 90], 1.0, 1.0, None, {'cm': [1, 1, 1, 1, 0, 1]}),
      ('Sunset', [300, 45, 345, 285, 0, 70], 1.0, 1.0, None, {'cm': [1.4, 1.6, 1, 1.3, 0, 1]}),
      ('Rose Gold', [30, 55, 15, 35, 0, 55], 1.0, 1.0, [300, 55, 15, 330, 0, 55], {'cm': [1.2, 1.4, .8, 1, 0, 7]}),
      ('Mercury', [250, 240, 230, 255, 250, 250], .25, 1.0, None, {'cm': [1, 1, 1, 1, 0, 1]})],
     ('depth', 'Depth', 'Flat', 'Domed', .55))

wall('velvet', 'Velvet', 'vibrant', 48,
     'Folds of heavy satin, jewel-dark in the shadows, with a sheen along every fold.',
     [(.12, .08, .42, .12), (.33, .17, .64, .14), (.56, .20, .82, .1), (.88, .07, .97, .03), (.30, .16, .58, .14), (.70, .12, .86, .08)],
     [('Garnet', [355, 350, 10, 20, 320, 45]),
      ('Sapphire', [265, 262, 255, 220, 285, 200]),
      ('Emerald', [160, 158, 150, 140, 180, 90]),
      ('Amethyst', [305, 300, 320, 290, 280, 340]),
      ('Platinum', [250, 255, 250, 240, 265, 220], .3)],
     ('folds', 'Folds', 'Broad', 'Fine', .45))

wall('spectra', 'Spectra', 'vibrant', 60,
     'A glass prism turning in a white beam, its spectrum sweeping through the haze.',
     [(.12, .03, .955, .01), (.17, .05, .92, .015), (.40, .06, .85, .03), (.74, .16, .74, .15), (.62, .17, .66, .15), (.98, .01, .995, .005)],
     [('Daylight', [270, 265, 260, 25, 300, 90]),
      ('Sunset', [300, 290, 330, 10, 110, 60]),
      ('Ocean', [240, 230, 220, 150, 290, 200]),
      ('Aurora', [270, 280, 300, 140, 330, 180]),
      ('Quiet', [265, 260, 260, 200, 320, 90], .55)],
     ('bright', 'Brightness', 'Dim', 'Bright', .5))

wall('bloom', 'Bloom', 'vibrant', 72,
     'A flower made of light: two rings of translucent petals sway and breathe.',
     [(.12, .045, .965, .012), (.19, .07, .93, .025), (.80, .14, .85, .11), (.63, .20, .74, .14), (.60, .18, .76, .12), (.96, .05, .99, .02)],
     [('Peony', [330, 320, 50, 350, 300, 80]),
      ('Lotus', [300, 330, 20, 330, 290, 60]),
      ('Marigold', [40, 30, 85, 35, 10, 95], 1.0, 1.0, [295, 320, 85, 35, 10, 95]),
      ('Hydrangea', [265, 250, 200, 275, 310, 190]),
      ('Quiet', [330, 320, 60, 340, 290, 80], .45, .8)],
     ('soft', 'Softness', 'Crisp', 'Feathered', .45))

wall('nebula', 'Nebula', 'vibrant', 90,
     'Glowing gas folded by slow currents, crossed by dark dust and a few still stars.',
     [(.10, .04, .975, .008), (.17, .07, .94, .02), (.56, .21, .62, .16), (.62, .15, .66, .12), (.72, .13, .70, .12), (.95, .06, .60, .08)],
     [('Orion', [280, 295, 345, 215, 60, 320]),
      ('Crab', [260, 240, 30, 200, 80, 50]),
      ('Veil', [240, 220, 190, 300, 170, 200]),
      ('Carina', [20, 10, 50, 330, 80, 40], 1.0, 1.0, [285, 310, 45, 330, 75, 40], {'dl': [0, 0, .04, 0, .04, 0]}),
      ('Quiet', [270, 280, 320, 230, 60, 300], .45)],
     ('density', 'Density', 'Sparse', 'Dense', .5))

wall('lagoon', 'Lagoon', 'vibrant', 40,
     'Sunlight focused by moving water onto a shallow floor, each line edged with colour.',
     [(.30, .12, .62, .11), (.52, .13, .84, .09), (.60, .07, .90, .05), (.97, .06, .99, .04), (.20, .08, .55, .08), (.99, .02, 1.0, .0)],
     [('Atoll', [235, 195, 85, 185, 245, 100]),
      ('Reef', [215, 175, 60, 160, 230, 90]),
      ('Grotto', [190, 165, 120, 150, 210, 120]),
      ('Moonpool', [265, 250, 270, 220, 280, 250]),
      ('Quiet', [225, 205, 80, 200, 235, 100], .45)],
     ('clarity', 'Clarity', 'Soft', 'Sharp', .65))

wall('iris', 'Iris', 'vibrant', 50,
     'A soap film turning slowly, its colours true thin-film interference.',
     [(.11, .02, .96, .01), (.20, .04, .93, .02), (.62, .19, .80, .11), (.66, .14, .82, .09), (.78, .15, .88, .1), (.98, .01, 1.0, .0)],
     [('Bubble', [270, 260, 345, 215, 80, 60]),
      ('Opal', [250, 240, 15, 175, 75, 60]),
      ('Oil Slick', [290, 280, 320, 245, 140, 60], 1.15),
      ('Frost', [235, 225, 290, 205, 180, 220]),
      ('Quiet', [260, 250, 330, 215, 95, 60], .5)],
     ('thickness', 'Thickness', 'Broad', 'Fine', .45))


# ================================================================ balanced
wall('dunes', 'Dunes', 'balanced', 60,
     'Ridge after ridge of sand under a low sun; the light slides across the faces.',
     [(.24, .06, .86, .05), (.50, .10, .93, .05), (.42, .08, .86, .05), (.64, .11, .87, .08), (.30, .08, .68, .08), (.90, .10, .98, .05)],
     [('Sahara', [275, 45, 330, 55, 315, 75]),
      ('Rose Dusk', [290, 10, 340, 30, 300, 50]),
      ('Mars', [20, 40, 25, 45, 10, 60], 1.0, 1.0, [285, 25, 330, 40, 345, 55], {'dl': [0, .04, .03, .06, .03, 0], 'dc': [1, 1.3, 1, 1.3, 1.2, 1]}),
      ('Moonlit', [265, 250, 260, 240, 275, 230], .5),
      ('Quiet', [270, 60, 320, 60, 300, 80], .4, .8)],
     ('ripples', 'Ripples', 'None', 'Fine', .6))

wall('tides', 'Tides', 'balanced', 45,
     'Five broad waves of colour laid one over another, rolling slowly.',
     [(.24, .08, .90, .04), (.32, .11, .84, .07), (.42, .13, .78, .09), (.54, .13, .72, .10), (.66, .12, .66, .11), (.92, .05, .98, .03)],
     [('Sonoma', [250, 230, 205, 175, 145, 200]),
      ('Coral', [300, 330, 355, 20, 45, 30], 1.0, 1.0, [285, 310, 340, 10, 35, 30], {'dl': [0, .02, .04, .05, .03, 0], 'dc': [1, 1.1, 1.2, 1.25, 1.2, 1]}),
      ('Dusk', [270, 290, 320, 350, 25, 40]),
      ('Glacier', [255, 240, 225, 210, 195, 220]),
      ('Quiet', [250, 240, 225, 205, 185, 210], .45, .8)],
     ('depth', 'Depth', 'Flat', 'Deep', .55))

wall('afterglow', 'Afterglow', 'balanced', 36,
     'Warm light leaking in at the edges, as on a roll of film, swelling and fading.',
     [(.17, .035, .95, .015), (.23, .05, .92, .025), (.7, .17, .72, .17), (.56, .2, .62, .19), (.50, .10, .76, .09), (.94, .08, .9, .09)],
     [('Amber', [30, 40, 60, 25, 230, 85], 1.0, 1.0, [285, 300, 62, 42, 230, 85], {'dl': [0, 0, .04, .06, 0, 0]}),
      ('Rose', [350, 0, 20, 355, 250, 50], 1.0, 1.0, [290, 305, 25, 0, 250, 50], {'dl': [0, 0, .05, .08, 0, 0]}),
      ('Magenta', [320, 330, 350, 330, 200, 30]),
      ('Teal Leak', [220, 210, 190, 170, 30, 120]),
      ('Quiet', [40, 50, 60, 30, 230, 80], .45, 1.0, [265, 280, 60, 30, 230, 80])],
     ('intensity', 'Intensity', 'Faint', 'Strong', .55))

wall('inkwell', 'Inkwell', 'balanced', 60,
     'Two inks dropped into still water, billowing into fine tendrils.',
     [(.15, .03, .97, .01), (.20, .04, .94, .015), (.55, .17, .45, .16), (.65, .18, .60, .17), (.80, .08, .80, .06), (.95, .04, .99, .02)],
     [('Indigo and Vermilion', [250, 240, 270, 35, 60, 80]),
      ('Teal and Rose', [220, 210, 195, 0, 60, 80]),
      ('Violet and Gold', [280, 270, 300, 75, 60, 90]),
      ('Sumi', [260, 250, 250, 220, 240, 80], .3),
      ('Quiet', [240, 230, 265, 30, 60, 80], .5)],
     ('density', 'Density', 'Veils', 'Plumes', .5))

wall('fluted', 'Fluted', 'balanced', 48,
     'Colour drifting behind a pane of reeded glass; the glass stays still.',
     [(.30, .06, .88, .04), (.55, .15, .78, .11), (.48, .15, .74, .12), (.62, .13, .84, .09), (.42, .14, .70, .12), (.95, .02, .99, .01)],
     [('Sorbet', [290, 350, 30, 70, 320, 0], 1.0, 1.0, [285, 350, 25, 55, 320, 0], {'dl': [0, .04, .08, .06, 0, 0], 'dc': [1, 1.15, 1.25, 1.2, 1, 1]}),
      ('Lagoon', [240, 200, 170, 230, 265, 0]),
      ('Garden', [150, 165, 125, 190, 140, 0]),
      ('Twilight', [275, 300, 255, 330, 240, 0]),
      ('Quiet', [280, 340, 30, 230, 300, 0], .45, .8)],
     ('reeds', 'Reeds', 'Fine', 'Wide', .4))

wall('meridian', 'Meridian', 'balanced', 90,
     'A slow-turning gradient of five colours, melting toward a soft light.',
     [(.48, .15, .80, .11), (.55, .15, .84, .10), (.62, .14, .86, .09), (.52, .16, .80, .11), (.44, .15, .76, .12), (.90, .06, .98, .03)],
     [('Studio', [250, 295, 340, 25, 300, 80], 1.0, 1.0, [255, 300, 345, 15, 305, 80]),
      ('Ember', [10, 35, 60, 350, 320, 70], 1.0, 1.0, [5, 25, 45, 345, 315, 60], {'dl': [.04, .06, .08, .04, .02, 0], 'dc': [1.15, 1.15, 1.15, 1.15, 1.15, 1]}),
      ('Ocean', [230, 250, 200, 180, 270, 210]),
      ('Orchid', [300, 330, 280, 350, 260, 320]),
      ('Quiet', [250, 290, 330, 290, 220, 80], .45, .8)],
     ('soft', 'Softness', 'Defined', 'Melted', .55))

wall('nacre', 'Nacre', 'balanced', 56,
     'Mother of pearl: thin layers that catch the light and shift hue as they drift.',
     [(.2, .045, .92, .015), (.5, .13, .86, .055), (.54, .12, .9, .05), (.48, .13, .85, .055), (.56, .12, .9, .05), (.85, .04, .99, .01)],
     [('Pearl', [300, 330, 220, 280, 175, 60], 1.0, 1.0, [285, 330, 215, 280, 185, 60]),
      ('Abalone', [190, 160, 220, 260, 130, 200]),
      ('Rose Shell', [10, 350, 30, 320, 50, 40], 1.0, 1.0, [320, 350, 15, 300, 340, 30], {'dl': [0, .04, .06, .02, .05, 0]}),
      ('Moonstone', [250, 240, 220, 270, 200, 230], .6, 1.0, [245, 230, 205, 275, 190, 230], {'dc': [1.4, 1.5, 1.5, 1.5, 1.5, 1]}),
      ('Quiet', [300, 330, 220, 280, 170, 60], .45, 1.0, None, {'dc': [1.1, .75, .75, .75, .75, 1]})],
     ('layers', 'Layers', 'Few', 'Many', .45))

wall('ribbon', 'Ribbon', 'balanced', 40,
     'Two satin ribbons floating across the window, twisting slowly as they go.',
     [(.20, .05, .95, .015), (.15, .04, .91, .025), (.62, .15, .72, .13), (.55, .16, .66, .14), (.45, .13, .58, .12), (.95, .04, .99, .02)],
     [('Satin', [260, 270, 15, 330, 280, 40]),
      ('Lagoon', [240, 230, 190, 220, 260, 180]),
      ('Saffron', [40, 30, 70, 40, 15, 90], 1.0, 1.0, [290, 300, 70, 40, 15, 90], {'dl': [0, 0, .04, .04, .04, 0]}),
      ('Iris', [285, 280, 300, 260, 320, 300]),
      ('Quiet', [260, 250, 20, 330, 280, 40], .45)],
     ('width', 'Width', 'Slim', 'Wide', .5))

# ================================================================ minimal
wall('mist', 'Mist', 'minimal', 90,
     'Coloured mist in four layers at different depths, the near veils sliding over the far.',
     [(.25, .05, .935, .025), (.31, .065, .91, .035), (.36, .075, .89, .045), (.41, .085, .87, .055), (.46, .095, .85, .06), (.5, .04, .97, .02)],
     [('Morning', [255, 220, 300, 200, 330, 80]),
      ('Lavender', [285, 300, 265, 320, 290, 300]),
      ('Sea Glass', [205, 180, 215, 165, 190, 180]),
      ('Peach', [50, 30, 55, 15, 70, 80], 1.0, 1.0, [300, 355, 25, 30, 340, 60],
       {'dl': [0, .06, .12, .12, .06, .05], 'dc': [.8, 1.2, 1.35, 1.35, 1.15, 1]}),
      ('Fog', [250, 240, 230, 260, 250, 250], .35)],
     ('depth', 'Depth', 'Close', 'Deep', .5), motion=.45)

wall('daybreak', 'Daybreak', 'minimal', 80,
     'A quiet horizon at first light, a low sun resting on still water.',
     [(.25, .06, .86, .04), (.32, .07, .90, .04), (.45, .08, .94, .05), (.27, .05, .88, .035), (.55, .08, .96, .05), (.75, .07, .99, .03)],
     [('First Light', [260, 290, 50, 250, 60, 80]),
      ('Rose', [280, 320, 15, 290, 30, 60]),
      ('Ember', [300, 330, 40, 280, 45, 70], 1.0, 1.0, None, {'dl': [0, 0, .05, 0, .03, 0], 'dc': [1, 1, 1.3, 1, 1.2, 1]}),
      ('Haze', [230, 220, 200, 230, 80, 90], .6),
      ('Quiet', [260, 280, 60, 250, 70, 80], .45)],
     ('glow', 'Glow', 'Faint', 'Warm', .5), motion=.45)

wall('stillwater', 'Stillwater', 'minimal', 80,
     'The light of a pool thrown softly onto a pale wall, never sharp.',
     [(.30, .04, .92, .03), (.26, .05, .89, .035), (.22, .04, .85, .03), (.50, .06, .98, .025), (.42, .07, .95, .04), (.60, .03, 1.0, .0)],
     [('Pool', [220, 240, 230, 195, 180, 200]),
      ('Celadon', [160, 180, 170, 150, 120, 150]),
      ('Blush', [10, 340, 350, 30, 50, 40], 1.0, 1.0, [310, 300, 295, 0, 20, 0], {'dl': [0, 0, 0, .06, .05, 0]}),
      ('Dusk', [270, 290, 280, 250, 320, 260]),
      ('Quiet', [230, 240, 240, 220, 210, 220], .4)],
     ('shimmer', 'Shimmer', 'Faint', 'Clear', .5), motion=.45)

wall('linen', 'Linen', 'minimal', 72,
     'A woven linen breathing in a faint draught, its threads catching the light.',
     [(.25, .04, .80, .035), (.32, .05, .89, .03), (.40, .045, .95, .02), (.33, .065, .89, .045), (.5, .03, .98, .015), (.45, .04, .97, .02)],
     [('Flax', [70, 70, 75, 30, 80, 80], 1.0, 1.0, [260, 255, 250, 300, 60, 60]),
      ('Sage', [140, 140, 130, 180, 120, 100], 1.0, 1.0, [170, 175, 160, 200, 150, 120]),
      ('Mauve', [330, 320, 330, 290, 340, 20]),
      ('Chambray', [250, 245, 240, 220, 250, 260]),
      ('Quiet', [70, 60, 70, 40, 80, 80], .5, 1.0, [260, 255, 250, 280, 60, 60])],
     ('weave', 'Weave', 'Fine', 'Coarse', .45), motion=.45)

wall('fields', 'Fields', 'minimal', 90,
     'Three soft fields of colour floating on a ground, breathing very slowly.',
     [(.26, .05, .90, .035), (.38, .09, .80, .08), (.30, .07, .86, .06), (.34, .09, .78, .08), (.44, .08, .88, .06), (.50, .03, .95, .02)],
     [('Ochre and Plum', [30, 25, 330, 300, 50, 60], 1.0, 1.0, [285, 30, 330, 295, 45, 60], {'dl': [0, .08, .03, .03, .08, 0], 'dc': [.8, 1.3, 1.1, 1.1, 1.2, 1]}),
      ('Seaside', [230, 200, 60, 215, 190, 220]),
      ('Orchard', [100, 70, 30, 130, 80, 90], 1.0, 1.0, [290, 25, 335, 160, 15, 90], {'dl': [0, .08, .03, .03, .08, 0], 'dc': [.8, 1.3, 1.1, 1.1, 1.2, 1]}),
      ('Dusk', [280, 330, 260, 300, 20, 300]),
      ('Quiet', [40, 30, 300, 230, 50, 60], .45, 1.0, [300, 330, 280, 230, 310, 260])],
     ('edges', 'Edges', 'Crisp', 'Feathered', .5), motion=.45)

wall('lumen', 'Lumen', 'minimal', 70,
     'A shaft of sunlight sweeping slowly across a wall, a soft caustic wavering in it.',
     [(.26, .04, .905, .022), (.21, .045, .88, .027), (.52, .12, .935, .11), (.4, .1, .86, .07), (.6, .09, .99, .06), (.50, .03, 1.0, .0)],
     [('Afternoon', [260, 250, 70, 240, 80, 80], 1.0, 1.0, [265, 255, 48, 235, 60, 80], {'dl': [0, 0, .08, 0, .08, 0], 'dc': [1, 1, 1.25, 1, 1.2, 1]}),
      ('Rose Hour', [290, 280, 25, 260, 40, 40], 1.0, 1.0, [290, 280, 0, 260, 20, 40], {'dl': [0, 0, .08, 0, .08, 0], 'dc': [1, 1, 1.2, 1, 1.2, 1]}),
      ('Citrus', [120, 110, 95, 200, 100, 100], 1.0, 1.0, [215, 220, 175, 240, 185, 160]),
      ('North Light', [230, 230, 210, 250, 200, 200], .7),
      ('Quiet', [250, 250, 70, 240, 70, 80], .45)],
     ('glow', 'Glow', 'Faint', 'Bright', .5), motion=.45)

wall('drift', 'Drift', 'minimal', 84,
     'Banks of coloured fog lying one behind another, rising and settling.',
     [(.25, .05, .92, .03), (.34, .06, .95, .03), (.38, .05, .86, .045), (.31, .065, .78, .06), (.23, .075, .68, .075), (.5, .07, .98, .03)],
     [('Highlands', [250, 230, 200, 170, 150, 70]),
      ('Lilac', [280, 300, 290, 320, 340, 40]),
      ('Harbour', [230, 210, 220, 200, 190, 200]),
      ('Autumn', [40, 60, 30, 20, 350, 70], 1.0, 1.0, [290, 315, 335, 22, 32, 45], {'dl': [0, 0, .03, .1, .17, 0], 'dc': [1, 1, 1.1, 1.45, 1.6, 1]}),
      ('Quiet', [250, 240, 230, 220, 210, 80], .45)],
     ('density', 'Density', 'Thin', 'Thick', .5), motion=.45)

wall('halo', 'Halo', 'minimal', 90,
     'One wide, soft ring of light, its colour turning slowly round it.',
     [(.20, .04, .94, .015), (.45, .10, .82, .07), (.45, .10, .82, .07), (.45, .10, .82, .07), (.30, .05, .90, .03), (.50, .03, .98, .0)],
     [('Prism', [270, 330, 200, 60, 280, 0]),
      ('Ember', [300, 20, 50, 340, 320, 0], 1.0, 1.0, [295, 15, 40, 340, 320, 0], {'dl': [0, .08, .08, .06, 0, 0], 'dc': [1, 1.25, 1.25, 1.2, 1, 1]}),
      ('Ocean', [240, 200, 260, 170, 230, 0]),
      ('Garden', [130, 90, 160, 60, 120, 0], 1.0, 1.0, [160, 195, 135, 225, 160, 0]),
      ('Quiet', [270, 330, 210, 70, 280, 0], .45)],
     ('width', 'Width', 'Fine', 'Wide', .5), motion=.45)


# ================================================================ the SkSL subset, checked
BANNED = [
    (r'\btexture\w*\s*\(|\btexelFetch\b|\bsampler', 'no texture lookups'),
    (r'\bdFd[xy]\b|\bfwidth\b', 'no derivatives'),
    (r'\bwhile\b|\bdo\b|\bswitch\b|\bdiscard\b', 'no while, do, switch or discard'),
    (r'#', 'no preprocessor'),
    (r'<<|>>|(?<![&|])[&|^](?![&|=])|~', 'no bit operations'),
    (r'%', 'no integer modulo'),
    (r'\buint\b|\buvec\d', 'no unsigned types'),
    (r'\b(tanh|sinh|cosh|asinh|acosh|atanh|round|roundEven|trunc|inverse|transpose|determinant|outerProduct|isnan|isinf|'
     r'floatBitsToInt|intBitsToFloat|packHalf2x16|unpackHalf2x16|modf|frexp|ldexp)\s*\(', 'ES 3 only built-ins'),
    (r'\b(vec[234]|float|int|mat[234])\s*\[\s*\d*\s*\]\s*\(', 'no array constructors'),
    (r'\bgl_\w+', 'no gl_ variables in the wallpaper'),
    (r'(?m)\buniform\b|^\s*(?:in|out)\s+(?:vec|float|int)', 'uniforms and varyings belong to the host'),
]
LOOP = re.compile(r'for\s*\(\s*int\s+(\w+)\s*=\s*0\s*;\s*\1\s*<\s*\d+\s*;\s*\1\+\+\s*\)')


def lint(name, src):
    code = re.sub(r'//[^\n]*', '', src)
    for rx, why in BANNED:
        m = re.search(rx, code)
        assert not m, (name, why, code[max(0, m.start() - 40):m.end() + 40])
    for m in re.finditer(r'\bfor\s*\(', code):
        assert LOOP.match(code, m.start()), (name, 'loops must be for (int i = 0; i < N; i++)', code[m.start():m.start() + 60])
    if name != 'common':
        # the clock is used once, as a = TAU * t / T: never t times a speed, never t on its own
        uses = re.findall(r'\bt\b', code)
        assert len(uses) == 2 and 'vec3 wall(vec2 p, float t)' in code and 'TAU * t / T' in code, (name, 'the clock must only appear as TAU * t / T')


HOST = {
    'signature': 'vec3 wall(vec2 p, float t) returns sRGB in 0..1; the host adds the dither',
    'coordinates': 'p = (fragCoord - 0.5 * resolution) * (2 / resolution.y), y up: the height spans -1..1 and x grows with the aspect',
    'clock': 't is the loop clock in seconds, 0 <= t < T (period). The host keeps a phase per wallpaper, phase += dt * speed, wrapped at T; '
             'changing the speed never moves the picture. All motion is built from a = TAU * t / T, its whole-number multiples and closed loops in noise space, '
             'so frame(t) == frame(t + T).',
    'uniforms': [
        {'name': 'u_pal', 'type': 'vec3[6]', 'sksl': 'uniform float3 u_pal[6]', 'meaning': 'the colour pack in OKLab: the host mixes the pack’s dark and light palettes for the theme, and eases from the old palette to the new on a pack or theme change (time constant 0.25 s)'},
        {'name': 'u_dark', 'type': 'float', 'sksl': 'uniform float u_dark', 'meaning': '0 light .. 1 dark, eased with the same time constant'},
        {'name': 'u_opt', 'type': 'vec2', 'sksl': 'uniform float2 u_opt', 'meaning': 'x = Motion (0 calm .. 1 lively), y = the wallpaper’s second option (0..1), each eased (0.3 s)'},
        {'name': 'u_px', 'type': 'float', 'sksl': 'uniform float u_px', 'meaning': 'one device pixel in p units, 2 / resolution.y, for anti-aliased edges'},
    ],
    'dither': 'triangular, one 8-bit step, fixed in screen space: (ign(fc) + ign(fc + (37, 17)) - 1) / 255 with ign(v) = fract(52.9829189 * fract(dot(v, (0.06711056, 0.00583715))))',
    'subset': 'GLSL ES 3.00 written to the SkSL runtime-effect subset: math only, no textures, no derivatives, no preprocessor, '
              'for (int i = 0; i < N; i++) loops only, no bit operations or integer modulo, uniform arrays indexed by loop indices only. '
              'Each file is checked against this list when the library is built.',
    'speed': 'Speed is per wallpaper, 0.25x to 2.5x, default 1x. It scales the phase clock only; easing it (0.18 s) keeps a change smooth.',
}


# ================================================================ output
def build():
    common = open(os.path.join(HERE, 'common.glsl'), encoding='utf-8').read()
    lint('common', common)
    assert len(W) == 24 and all(sum(1 for w in W if w['tier'] == t) == 8 for t in ('vibrant', 'balanced', 'minimal'))
    out = []
    for w in W:
        src = open(os.path.join(HERE, 'walls', w['id'] + '.glsl'), encoding='utf-8').read()
        lint(w['id'], src)
        assert 20 <= w['period'] <= 90 and 4 <= len(w['packs']) <= 6
        m = re.search(r'const float T = ([0-9.]+);', src)
        assert m and abs(float(m.group(1)) - w['period']) < 1e-9, (w['id'], 'period in the shader differs')
        packs = [make_pack(w['roles'], *p) for p in w['packs']]
        out.append(dict(id=w['id'], name=w['name'], tier=w['tier'], period=w['period'], desc=w['desc'],
                        defaults=dict(pack=packs[0]['id'], speed=1.0, motion=w['options'][0]['default'], second=w['options'][1]['default']),
                        options=w['options'], packs=packs, glsl=src))
    return common, out


if __name__ == '__main__':
    common, out = build()
    json.dump({'name': 'Marrow animated wallpapers', 'version': '2026-10-05', 'host': HOST, 'common': common, 'wallpapers': out},
              open(os.path.join(HERE, 'wallpapers.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=True)
    print(len(out), 'wallpapers')
