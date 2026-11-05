"""STG Phantom VPN brand marks as SVG strings (original artwork).

The STG lettering follows the STG Weather launcher mark: flat block letters cut by thin straight slits.
Here the S reads the right way round and the G has a crossbar; a white shield with a mint "phantom" echo
behind it takes the place of Weather's cloud in front of the sun.
"""

TEAL = {'950': '#042F2B', '900': '#064E47', '800': '#0A6158', '700': '#0B7A70', '600': '#0E9688',
        '500': '#14B8A6', '400': '#2DD4BF', '300': '#5EEAD4', '200': '#99F6E4', '100': '#CCFBF1', '50': '#F0FDFA'}
SLATE = {'700': '#3F524F', '600': '#566966', '500': '#6E817E'}
WHITE = '#FFFFFF'


def squircle(x0, y0, size, r):
    x1, y1 = x0 + size, y0 + size
    return ('M{a} {y0} H{b} C{c} {y0} {x1} {d} {x1} {e} V{f} C{x1} {g} {c} {y1} {b} {y1} H{a} '
            'C{h} {y1} {x0} {g} {x0} {f} V{e} C{x0} {d} {h} {y0} {a} {y0} Z').format(
        a=x0 + r, b=x1 - r, c=x1 - r * 0.2, d=y0 + r * 0.2, e=y0 + r, f=y1 - r, g=y1 - r * 0.2, h=x0 + r * 0.2,
        x0=x0, y0=y0, x1=x1, y1=y1)


def shield(cx, top, w, h):
    """Shield outline: flat shoulders, straight sides, curved point. Same shape as the in-app shield glyphs."""
    l, r, b = cx - w / 2, cx + w / 2, top + h
    s = h * 0.16   # shoulder drop
    side = top + h * 0.46
    return ('M{cx} {top} L{r} {ts} V{side} C{r} {c1} {c2r} {c3} {cx} {b} C{c2l} {c3} {l} {c1} {l} {side} V{ts} Z').format(
        cx=cx, top=top, r=r, l=l, ts=top + s, side=side, c1=top + h * 0.74, c2r=cx + w * 0.30, c2l=cx - w * 0.30,
        c3=top + h * 0.92, b=b)


def letter_rects(x, y, h):
    """Rectangles (x, y, w, h) of the block S, T and G, with the letter width, at height h."""
    w = h * 0.86
    sl = h * 0.13                # slit
    bar = (h - 2 * sl) / 3       # S: three equal bars
    st = w * 0.27                # vertical strokes
    stem = w * 0.34
    S = [(0, 0, w, bar), (0, 0, st, 2 * bar + sl), (0, bar + sl, w, bar), (w - st, bar + sl, st, 2 * bar + sl), (0, h - bar, w, bar)]
    T = [(0, 0, w, bar + sl * 0.35), ((w - stem) / 2, 0, stem, h)]
    G = [(0, 0, w, bar), (0, 0, st, h), (0, h - bar, w, bar), (w - st, bar + sl, st, h - bar - sl), (st + sl, bar + sl, w - st - sl, bar)]
    return [S, T, G], w


def letters(x, y, h, gap, color, clip_id):
    """Block STG of height h at (x, y). Returns (defs, body, total width); outer corners rounded by a clip."""
    groups, w = letter_rects(x, y, h)
    r = h * 0.10
    defs, body = [], []
    for i, g in enumerate(groups):
        X = x + i * (w + gap)
        cid = '%s%d' % (clip_id, i)
        defs.append('<clipPath id="%s"><rect x="%g" y="%g" width="%g" height="%g" rx="%g"/></clipPath>' % (cid, X, y, w, h, r))
        body.append('<g fill="%s" clip-path="url(#%s)">%s</g>' % (color, cid, ''.join(
            '<rect x="%g" y="%g" width="%g" height="%g"/>' % (X + a, y + b, c, d) for (a, b, c, d) in g)))
    body = ''.join(body)
    return '\n'.join(defs), body, 3 * w + 2 * gap


def check(cx, cy, size):
    k = size / 24.0
    return 'M{} {} L{} {} L{} {}'.format(cx - 6 * k, cy + 0.5 * k, cx - 1.8 * k, cy + 4.6 * k, cx + 6.4 * k, cy - 4 * k)


def app_icon(kind, state='brand'):
    """kind: full (>= 48 px), small (20-40 px), tiny (16 px). state: brand, idle (tray, disconnected), connected (tray)."""
    V = 256.0
    m = {'full': 10, 'small': 4, 'tiny': 0}[kind]
    size = V - 2 * m
    top, bottom = (TEAL['500'], TEAL['700']) if state != 'idle' else (SLATE['500'], SLATE['700'])
    o = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">', '<defs>',
         '<linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="%s"/><stop offset="1" stop-color="%s"/></linearGradient>' % (top, bottom),
         '<linearGradient id="hl" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity="0.22"/><stop offset="0.55" stop-color="#fff" stop-opacity="0"/></linearGradient>']
    body = []
    sq = squircle(m, m, size, size * 0.30)
    if kind == 'full':
        for dy, op in ((6, 0.06), (3, 0.08)):
            body.append('<path d="%s" fill="%s" fill-opacity="%s" transform="translate(0 %d)"/>' % (sq, TEAL['950'], op, dy))
    body.append('<path d="%s" fill="url(#bg)"/>' % sq)
    if kind != 'tiny':
        body.append('<path d="%s" fill="url(#hl)"/>' % sq)
    echo = TEAL['300'] if state != 'idle' else '#B8C7C4'
    if kind == 'full' and state == 'brand':
        body.append('<path d="%s" fill="%s"/>' % (shield(116, 28, 86, 98), echo))
        body.append('<path d="%s" fill="%s"/>' % (shield(140, 36, 86, 98), WHITE))
        lh, gap = 52, 10
        defs, ltr, _ = letters((V - (3 * lh * 0.86 + 2 * gap)) / 2, 156, lh, gap, WHITE, 'stg')
        o.append(defs)
        body.append(ltr)
        body.append('<path d="%s" fill="none" stroke="%s" stroke-width="11" stroke-linecap="round" stroke-linejoin="round"/>' % (check(140, 81, 64), TEAL['600']))
    else:
        # Small sizes: the shield alone, sized up; the echo only where it stays crisp.
        cx, t, w, h = 128, 40, 150, 172
        if kind == 'small':
            body.append('<path d="%s" fill="%s"/>' % (shield(cx - 18, t - 6, w, h), echo))
            cx, t = 140, 46
        if state == 'idle':
            sw = {'small': 22, 'tiny': 26}.get(kind, 18)
            body.append('<path d="%s" fill="none" stroke="%s" stroke-width="%d" stroke-linejoin="round"/>' % (shield(cx, t + sw / 2, w - sw, h - sw), WHITE, sw))
        else:
            body.append('<path d="%s" fill="%s"/>' % (shield(cx, t, w, h), WHITE))
            sw = {'small': 26, 'tiny': 30, 'full': 22}[kind]
            if state == 'connected' or kind != 'full':
                body.append('<path d="%s" fill="none" stroke="%s" stroke-width="%d" stroke-linecap="round" stroke-linejoin="round"/>'
                            % (check(cx, t + h * 0.44, 100), TEAL['700'], sw))
    o.append('</defs>')
    o += body
    o.append('</svg>')
    return '\n'.join(o)


def wizard_panel(w, h):
    """Inno Setup side panel: teal gradient, echo shield over block STG, scaled to the panel width."""
    k = w / 164.0
    o = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %g %g" width="%g" height="%g">' % (w, h, w, h), '<defs>',
         '<linearGradient id="bg" x1="0" y1="0" x2="0.4" y2="1"><stop offset="0" stop-color="%s"/><stop offset="1" stop-color="%s"/></linearGradient>' % (TEAL['500'], TEAL['800'])]
    cx, top = w / 2, h * 0.30
    sw, sh = 70 * k, 80 * k
    lh, gap = 30 * k, 6 * k
    lw = 3 * lh * 0.86 + 2 * gap
    defs, ltr, _ = letters(cx - lw / 2, top + sh + 22 * k, lh, gap, WHITE, 'wz')
    o += [defs, '</defs>', '<rect width="%g" height="%g" fill="url(#bg)"/>' % (w, h),
          '<path d="%s" fill="%s"/>' % (shield(cx - 10 * k, top - 6 * k, sw, sh), TEAL['300']),
          '<path d="%s" fill="%s"/>' % (shield(cx + 10 * k, top, sw, sh), WHITE),
          '<path d="%s" fill="none" stroke="%s" stroke-width="%g" stroke-linecap="round" stroke-linejoin="round"/>' % (check(cx + 10 * k, top + sh * 0.46, 52 * k), TEAL['600'], 9 * k),
          ltr, '</svg>']
    return '\n'.join(o)
