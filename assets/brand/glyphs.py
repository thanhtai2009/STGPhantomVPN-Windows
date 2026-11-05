"""In-app glyphs on a 24 grid (original artwork, Material Symbols "Rounded"-like: one 2 px stroke, round caps
and joins). Parts: 's' stroke and 'f' fill in the foreground brush, 'k' stroke in the inner brush (a check
knocked out of a filled shield). The shield is the same outline as the logo; list markers and the
network nodes are the logo's small rounded blocks."""
import math

from marks import shield


def gear(cx=12.0, cy=12.0, r_out=8.6, r_in=6.6, teeth=8):
    pts = []
    for i in range(teeth):
        a = 2 * math.pi * i / teeth
        half = math.pi / teeth
        for da, r in ((-half * 0.95, r_in), (-half * 0.42, r_out), (half * 0.42, r_out), (half * 0.95, r_in)):
            pts.append((cx + r * math.cos(a + da), cy + r * math.sin(a + da)))
    return 'M' + ' L'.join('%.2f %.2f' % p for p in pts) + ' Z'


def circle(cx, cy, r):
    return 'M{a} {cy} A{r} {r} 0 1 1 {b} {cy} A{r} {r} 0 1 1 {a} {cy} Z'.format(a=cx - r, b=cx + r, cy=cy, r=r)


def rrect(x, y, w, h, r):
    return ('M{a} {y} H{b} A{r} {r} 0 0 1 {x1} {c} V{d} A{r} {r} 0 0 1 {b} {y1} H{a} A{r} {r} 0 0 1 {x} {d} V{c} A{r} {r} 0 0 1 {a} {y} Z'
            .format(a=x + r, b=x + w - r, c=y + r, d=y + h - r, x=x, y=y, x1=x + w, y1=y + h, r=r))


def fmt(d):
    """Shorter numbers: 2 decimals at most."""
    import re
    return re.sub(r'-?\d+\.\d+', lambda m: ('%.2f' % float(m.group())).rstrip('0').rstrip('.'), d)


SHIELD = fmt(shield(12, 2.6, 16, 18.8))
SHIELD_SMALL = fmt(shield(12, 4.2, 13, 15.6))
CHECK = 'M8 12.3 L10.9 15.1 L16.2 9.6'

GLYPHS = {
    'Home': ['s M3.8 10.6 L12 3.9 L20.2 10.6', 's M6 9.1 V18.6 A1.6 1.6 0 0 0 7.6 20.2 H10 V15.2 A1 1 0 0 1 11 14.2 H13 A1 1 0 0 1 14 15.2 V20.2 H16.4 A1.6 1.6 0 0 0 18 18.6 V9.1'],
    'Servers': ['f ' + rrect(3.5, 5, 3.6, 3.6, 1), 'f ' + rrect(3.5, 10.2, 3.6, 3.6, 1), 'f ' + rrect(3.5, 15.4, 3.6, 3.6, 1),
                's M10.5 6.8 H20.2', 's M10.5 12 H20.2', 's M10.5 17.2 H17'],
    'Settings': ['s ' + gear(), 's ' + circle(12, 12, 2.9)],
    'Shield': ['s ' + SHIELD],
    'ShieldCheck': ['f ' + SHIELD, 'k ' + CHECK],
    'ShieldLock': ['s ' + SHIELD, 'f ' + rrect(8.6, 10.6, 6.8, 5.4, 1.2), 's M10 10.6 V9.4 A2 2 0 0 1 14 9.4 V10.6'],
    'Power': ['s M12 3.8 V11.4', 's M7.3 6.9 A7.2 7.2 0 1 0 16.7 6.9'],
    'Stop': ['s ' + circle(12, 12, 8.3), 'f ' + rrect(8.8, 8.8, 6.4, 6.4, 1.4)],
    'Busy': ['s M7 3.8 H17', 's M7 20.2 H17', 's M8.2 3.8 V6.6 C8.2 9.2 12 10.4 12 12 C12 13.6 8.2 14.8 8.2 17.4 V20.2',
             's M15.8 3.8 V6.6 C15.8 9.2 12 10.4 12 12 C12 13.6 15.8 14.8 15.8 17.4 V20.2'],
    'ChevronRight': ['s M9.5 5.8 L15.7 12 L9.5 18.2'],
    'Download': ['s M12 3.8 V14.2', 's M7.6 9.9 L12 14.3 L16.4 9.9', 's M4.8 16.6 V18.5 A1.7 1.7 0 0 0 6.5 20.2 H17.5 A1.7 1.7 0 0 0 19.2 18.5 V16.6'],
    'Refresh': ['s M19.2 12 A7.2 7.2 0 1 1 16.9 6.7', 's M17.6 3.4 V7.4 H13.6'],
    'CheckCircle': ['s ' + circle(12, 12, 8.4), 's ' + CHECK],
    'Appearance': ['s ' + circle(12, 12, 8.4), 'f M12 5.6 A6.4 6.4 0 0 1 12 18.4 Z'],
    'Network': ['s ' + rrect(9.4, 3.6, 5.2, 4.4, 1.2), 's ' + rrect(3.6, 16, 5.2, 4.4, 1.2), 's ' + rrect(15.2, 16, 5.2, 4.4, 1.2),
                's M12 8 V12', 's M6.2 16 V12 H17.8 V16'],
    'Info': ['s ' + circle(12, 12, 8.4), 's M12 11 V16.4', 'f ' + circle(12, 7.8, 1.25)],
    'Heart': ['s M12 19.6 C9.4 18 4 14.4 4 9.4 A4.1 4.1 0 0 1 12 7.6 A4.1 4.1 0 0 1 20 9.4 C20 14.4 14.6 18 12 19.6 Z'],
    'Globe': ['s ' + circle(12, 12, 8.4), 's M12 3.6 C9.2 6.4 9.2 17.6 12 20.4 C14.8 17.6 14.8 6.4 12 3.6 Z', 's M3.8 12 H20.2'],
    'Timer': ['s ' + circle(12, 13.2, 7.4), 's M12 9.4 V13.6 L14.4 15', 's M10 2.8 H14', 's M18.4 5.6 L19.6 6.8'],
    'Windows': ['f ' + rrect(3.6, 3.6, 7.6, 7.6, 1.2), 'f ' + rrect(12.8, 3.6, 7.6, 7.6, 1.2),
                'f ' + rrect(3.6, 12.8, 7.6, 7.6, 1.2), 'f ' + rrect(12.8, 12.8, 7.6, 7.6, 1.2)],
    'OpenInNew': ['s M14 3.8 H20.2 V10', 's M20 4 L12 12', 's M18 14 V18.5 A1.7 1.7 0 0 1 16.3 20.2 H5.5 A1.7 1.7 0 0 1 3.8 18.5 V7.7 A1.7 1.7 0 0 1 5.5 6 H10'],
    'Certificate': ['s ' + circle(12, 9.6, 5.6), 's M8.9 14.3 L8 20.6 L12 18.6 L16 20.6 L15.1 14.3'],
    'Dns': ['s ' + rrect(3.8, 4.2, 16.4, 6.4, 1.8), 's ' + rrect(3.8, 13.4, 16.4, 6.4, 1.8),
            'f ' + circle(7.4, 7.4, 1.15), 'f ' + circle(7.4, 16.6, 1.15), 's M11.4 7.4 H16.4', 's M11.4 16.6 H16.4'],
    'Edit': ['s M4.4 19.6 L5.3 15.6 L15.6 5.3 A1.9 1.9 0 0 1 18.3 5.3 L18.7 5.7 A1.9 1.9 0 0 1 18.7 8.4 L8.4 18.7 Z', 's M13.8 7.1 L16.9 10.2'],
    'Adapter': ['s ' + rrect(3.6, 5.6, 16.8, 10, 1.8), 'f ' + rrect(7, 8.8, 4.4, 3.6, 0.8), 's M7.4 15.6 V19', 's M12 15.6 V19', 's M16.6 15.6 V19'],
    'Copy': ['s ' + rrect(8.6, 8.6, 11.6, 11.6, 1.8), 's M15.4 5.6 V5.4 A1.6 1.6 0 0 0 13.8 3.8 H5.4 A1.6 1.6 0 0 0 3.8 5.4 V13.8 A1.6 1.6 0 0 0 5.4 15.4 H5.6'],
    'ShieldX': ['f ' + SHIELD, 'k M9.6 9.4 L14.4 14.2', 'k M14.4 9.4 L9.6 14.2'],
    'Exit': ['s M13.4 4 H6.4 A1.6 1.6 0 0 0 4.8 5.6 V18.4 A1.6 1.6 0 0 0 6.4 20 H13.4', 's M10.2 12 H19.8', 's M16.4 8.6 L19.8 12 L16.4 15.4'],
    'Log': ['s ' + rrect(5, 3.6, 14, 16.8, 2), 's M8.6 8.4 H15.4', 's M8.6 12 H15.4', 's M8.6 15.6 H12.8'],
    'Trash': ['s M4.2 6.6 H19.8', 's M9.4 6.6 V4.8 A1 1 0 0 1 10.4 3.8 H13.6 A1 1 0 0 1 14.6 4.8 V6.6',
              's M6.2 6.6 L7.1 18.8 A1.6 1.6 0 0 0 8.7 20.2 H15.3 A1.6 1.6 0 0 0 16.9 18.8 L17.8 6.6', 's M10.2 10.6 V16.2', 's M13.8 10.6 V16.2'],
    'Search': ['s ' + circle(10.6, 10.6, 6.2), 's M15.2 15.2 L20 20'],
    'ChevronDown': ['s M6 9.4 L12 15.4 L18 9.4'],
    'Warning': ['s M10.3 4.6 A2 2 0 0 1 13.7 4.6 L20.6 17.2 A2 2 0 0 1 18.9 20.2 H5.1 A2 2 0 0 1 3.4 17.2 Z', 's M12 9.4 V13.4', 'f ' + circle(12, 16.6, 1.2)],
    'ErrorCircle': ['s ' + circle(12, 12, 8.4), 's M9.4 9.4 L14.6 14.6', 's M14.6 9.4 L9.4 14.6'],
    'Dismiss': ['s M6.4 6.4 L17.6 17.6', 's M17.6 6.4 L6.4 17.6'],
}
