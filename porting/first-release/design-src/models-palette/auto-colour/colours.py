import json,glob,math,io,re,os,sys
from PIL import Image
sys.path.insert(0,'.')
# dominant() and colour maths copied verbatim from the OD-0050 prototype (derive.py) so results are comparable
def srgb_to_lin(c):
    c=c/255; return c/12.92 if c<=0.04045 else ((c+0.055)/1.055)**2.4
def to_oklab(rgb):
    r,g,b=(srgb_to_lin(v) for v in rgb)
    l=0.4122214708*r+0.5363325363*g+0.0514459929*b; m=0.2119034982*r+0.6806995451*g+0.1073969566*b; s=0.0883024619*r+0.2817188376*g+0.6299787005*b
    l,m,s=(math.copysign(abs(v)**(1/3),v) for v in (l,m,s))
    return (0.2104542553*l+0.7936177850*m-0.0040720468*s,1.9779984951*l-2.4285922050*m+0.4505937099*s,0.0259040371*l+0.7827717662*m-0.8086757660*s)
def lch(rgb):
    L,a,b=to_oklab(rgb); return L,math.hypot(a,b),math.degrees(math.atan2(b,a))%360
def lin_to_srgb(c):
    c=max(0.0,min(1.0,c)); return round(255*(12.92*c if c<=0.0031308 else 1.055*c**(1/2.4)-0.055))
def from_lch(L,C,h):
    a=C*math.cos(math.radians(h)); b=C*math.sin(math.radians(h))
    l=(L+0.3963377774*a+0.2158037573*b)**3; m=(L-0.1055613458*a-0.0638541728*b)**3; s=(L-0.0894841775*a-1.2914855480*b)**3
    return (lin_to_srgb(4.0767416621*l-3.3077115913*m+0.2309699292*s),lin_to_srgb(-1.2684380046*l+2.6097574011*m-0.3413193965*s),lin_to_srgb(-0.0041960863*l-0.7034186147*m+1.7076147010*s))
def hexc(rgb): return '#%02X%02X%02X'%rgb
def dominant_img(img):
    img=img.convert('RGBA').resize((64,64))
    px=[p[:3] for p in img.getdata() if p[3]>=160]
    if not px: return None,'empty'
    colourful=[]
    for p in px:
        L,C,h=lch(p)
        if C>=0.06 and 0.25<L<0.95: colourful.append((L,C,h))
    if len(colourful)>=0.08*len(px):
        bins=[0.0]*24
        for L,C,h in colourful: bins[int(h//15)%24]+=C
        smooth=[bins[i-1]*0.5+bins[i]+bins[(i+1)%24]*0.5 for i in range(24)]
        best=max(range(24),key=lambda i:smooth[i])
        members=[(L,C,h) for L,C,h in colourful if min(abs(h-(best*15+7.5)),360-abs(h-(best*15+7.5)))<=22.5]
        sx=sum(C*math.cos(math.radians(h)) for L,C,h in members); sy=sum(C*math.sin(math.radians(h)) for L,C,h in members)
        hue=math.degrees(math.atan2(sy,sx))%360
        Ls=sorted(L for L,C,h in members); Cs=sorted(C for L,C,h in members)
        return (Ls[len(Ls)//2],Cs[int(len(Cs)*0.75)],hue),'colour'
    Ls=sorted(lch(p)[0] for p in px)
    return (Ls[len(Ls)//2],0.0,0.0),'mono'
def dom_file(p):
    try:
        if p.endswith('.svg'):
            t=open(p,encoding='utf-8',errors='ignore').read()
            cols=re.findall(r'(?:fill|stop-color)="(#[0-9a-fA-F]{6})"',t)
            cols=[c for c in cols if c.lower() not in('#ffffff','#000000')]
            return (cols[0].upper() if cols else None),'svg-fill'
        im=Image.open(p)
        if p.endswith('.ico'):
            sizes=sorted(im.ico.sizes()); im=im.ico.getimage(sizes[-1])
        d,k=dominant_img(im)
        if d is None: return None,k
        return (hexc(from_lch(*d)) if k=='colour' else 'mono L%.2f'%d[0]),k
    except Exception as e: return 'ERR '+type(e).__name__,'err'
if __name__=='__main__':
    from probe import MAKERS
    cols=['or_icon','google_faviconV2_256','site_declared_icon','hf_avatar','wikidata_logo','lobehub_color_svg']
    print('%-15s'%'maker',''.join('%-16s'%c[:15] for c in cols),'lobehub-toc','theme-color')
    for s in MAKERS:
        r=json.load(open(f'results/{s}.json',encoding='utf-8'))
        row=[]
        for c in cols:
            fs=glob.glob(f'samples/{s}/{c}.*'); row.append(dom_file(fs[0])[0] if fs else '-')
        print('%-15s'%s,''.join('%-16s'%str(x) for x in row),r['lobehub'].get('color'),r['site'].get('theme_color'))
