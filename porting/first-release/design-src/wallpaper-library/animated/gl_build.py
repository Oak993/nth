# Assembles lib-gl.js: the engine with the glass shader (adapted from the 2026-10-05 wallpapers page, which reuses the
# two MIT repos) and their licence notices put in place.
import os
HERE = os.path.dirname(os.path.abspath(__file__))


def built_gl():
    js = open(os.path.join(HERE, 'lib-gl.js'), encoding='utf-8').read()
    notice = open(os.path.join(HERE, '_glass_notice.txt'), encoding='utf-8').read()
    notice = notice.replace(
        notice[notice.index('* Wallpapers behind'):notice.index('*\n * The glass shader')],
        '* Animated wallpaper library: the WebGL2 engine. One shared context draws every visible tile and copies it into the\n'
        ' * tile\'s own canvas; the large view draws the wallpaper into a texture and refracts it through the mock window\'s\n'
        ' * glass, the way the two reference repos do. The same engine runs the loop proofs and the frame-cost measurement.\n ')
    glass = open(os.path.join(HERE, '_glass.glsl'), encoding='utf-8').read().replace('${MAXN}', '16')
    assert '`' not in glass and '${' not in glass
    return js.replace('@@NOTICE@@', notice).replace('@@GLASS@@', glass)


if __name__ == '__main__':
    open(os.path.join(HERE, '_dev', 'lib-gl.js'), 'w', encoding='utf-8', newline='\n').write(built_gl())
    print('ok')
