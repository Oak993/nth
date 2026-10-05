# Helpers shared by catalog.py and the tier catalogues (catalog_<tier>_2.py). This module imports nothing from the
# catalogue, so a tier file may do:   from wallpaper_kit import wall, dyn
# and export   WALLS = [wall(...), wall(...), ...]   (see OPTIONS-FORMAT.md).
#
# wall() returns the entry as a plain dict; catalog.py turns it into the shipped data (colour packs, the normalised
# premium options) and checks it against the shader.

MOTION = ('motion', 'Motion', 'Calm', 'Lively')
KEYS = ('dawn', 'day', 'dusk', 'night')

# A look is a change to the six palette colours, made in OKLCH:
#   dL    lightness added          (a number, or a list of six, one per palette slot)
#   kC    chroma multiplied        (a number, or six)
#   dh    hue rotated, in degrees  (a number, or six)
#   tint  (hue in degrees, amount 0..1): every colour is mixed that fraction of the way toward a colour of the tint hue
#         (chroma 0.12) in the OKLab a/b plane, as a photo filter does, and the host gives back 60% of the chroma the mix took
# The day look is the light theme's own palette (an empty look); the night look is the dark theme's own palette. The
# other three looks are what moves.
def _L(dL=0.0, kC=1.0, dh=0.0, tint=(0.0, 0.0)):
    return dict(dL=dL, kC=kC, dh=dh, tint=tint)


_SKY = dict(
    light=dict(dawn=_L(-.015, 1.10, 0, (355, .24)), day=_L(), dusk=_L(-.035, 1.16, 0, (335, .30)), night=_L(-.105, .90, 0, (262, .46))),
    dark=dict(dawn=_L(.030, 1.12, 0, (355, .24)), day=_L(.085, .92, 0, (232, .18)), dusk=_L(.030, 1.18, 0, (335, .32)), night=_L()),
)
_WARM = dict(
    light=dict(dawn=_L(-.015, 1.12, 0, (8, .30)), day=_L(), dusk=_L(-.040, 1.18, 0, (350, .34)), night=_L(-.105, .90, 0, (268, .42))),
    dark=dict(dawn=_L(.035, 1.14, 0, (8, .30)), day=_L(.085, .92, 0, (60, .14)), dusk=_L(.035, 1.20, 0, (350, .36)), night=_L()),
)
_GLOW = dict(
    light=dict(dawn=_L(-.010, 1.06, 0, (355, .22)), day=_L(), dusk=_L(-.020, 1.12, 0, (335, .28)), night=_L(-.075, 1.06, 0, (270, .40))),
    dark=dict(dawn=_L(.025, 1.06, 0, (355, .26)), day=_L(.065, .90, 0, (225, .30)), dusk=_L(.020, 1.12, 0, (335, .30)), night=_L()),
)
PRESETS = {'sky': _SKY, 'warm': _WARM, 'glow': _GLOW}
# the light's track across the day, in p units added to the wallpaper's own light position (its u_sun offset)
SUN_TRACK = dict(dawn=(-0.45, -0.12), day=(0.0, 0.0), dusk=(0.45, -0.12), night=(0.0, -0.22))
SUN_NONE = dict(dawn=(0.0, 0.0), day=(0.0, 0.0), dusk=(0.0, 0.0), night=(0.0, 0.0))


def _six(v):
    return [float(x) for x in v] if isinstance(v, (list, tuple)) else [float(v)] * 6


def _norm(look):
    return dict(dL=_six(look['dL']), kC=_six(look['kC']), dh=_six(look['dh']), tint=[float(look['tint'][0]), float(look['tint'][1])])


def dyn(preset='sky', scale=1.0, sun=None, light=None, dark=None):
    """The dynamic (time of day) data of a wallpaper.
    preset: 'sky' (light follows the sun: pink dawn, clear day, violet dusk, blue night), 'warm' (film and desert warmth),
            'glow' (self-luminous pictures, which change less).
    scale:  0..1.5, how far the looks move (minimal wallpapers use about 0.6).
    sun:    None when the wallpaper has no light direction, or 'track' for the standard sun track, or a dict of the four
            keys to (dx, dy) offsets; a wallpaper that reads u_sun must pass this.
    light, dark: optional dicts {'dusk': {'tint': (330, .3), 'dL': -.04}} that replace fields of one look of that theme."""
    base = PRESETS[preset]
    out = {}
    for th, over in (('light', light or {}), ('dark', dark or {})):
        out[th] = {}
        for k in KEYS:
            l = dict(base[th][k])
            l.update(over.get(k, {}))
            dL, kC, dh, tint = l['dL'], l['kC'], l['dh'], l['tint']
            if scale != 1.0:
                dL = [x * scale for x in _six(dL)]
                kC = [1 + (x - 1) * scale for x in _six(kC)]
                dh = [x * scale for x in _six(dh)]
                tint = (tint[0], tint[1] * scale)
            out[th][k] = _norm(dict(dL=dL, kC=kC, dh=dh, tint=tint))
    track = SUN_NONE if sun is None else (SUN_TRACK if sun == 'track' else sun)
    out['sun'] = {k: [float(track[k][0]), float(track[k][1])] for k in KEYS}
    return out


def wall(id, name, tier, period, desc, roles, packs, opt2, motion=0.5, dynamic=None, perspective='pan', focus=0.0, match=None):
    """One wallpaper entry.
    roles, packs, opt2, motion: exactly as in catalog.py.
    dynamic:     dyn(...) (default dyn('sky')).
    perspective: 'layers' when wall() calls tiltD(depth) for its layers (on by default), or 'pan' for a flat wallpaper
                 (off by default, and the host pans the whole picture a few pixels when the viewer turns it on).
    focus:       the default Focus, 0 (sharp) .. 0.6; use 0 unless the wallpaper is meant to be shown a little soft.
    match:       None (derived from the first pack) or dict(off=[six hue offsets in degrees from the accent], hero=slot,
                 ck=..., lk=..., dl=[six], dc=[six], cm=[six]); see OPTIONS-FORMAT.md."""
    assert perspective in ('pan', 'layers')
    return dict(id=id, name=name, tier=tier, period=period, desc=desc, roles=roles, packs=packs,
                options=[dict(zip(('id', 'label', 'lo', 'hi'), MOTION), default=motion),
                         dict(zip(('id', 'label', 'lo', 'hi'), opt2[:4]), default=opt2[4])],
                dynamic=dynamic if dynamic is not None else dyn('sky'),
                perspective=perspective, focus=focus, match=match)
