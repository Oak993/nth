// Builds src/Marrow.UI/Assets/naturalearth/world.nemap, the offline basemap of nth-ui's `map` (DESIGN.md 7.2, W16c).
//
// Input: three Natural Earth GeoJSON files at 1:50m (public domain, https://www.naturalearthdata.com/about/terms-of-use/), as
// published by https://github.com/nvkelso/natural-earth-vector (geojson/ne_50m_*.geojson):
//   ne_50m_admin_0_countries.geojson, ne_50m_lakes.geojson, ne_50m_populated_places_simple.geojson
// Output: one small binary (format "NEM1", little-endian varints), read by Marrow.UI/NthUi/Map/NthUiMapData.cs.
//
//   node tools/naturalearth/build.mjs <folder with the three files> [output file]
//
// Coordinates are quantised to 0.001 degree (about 110 m, finer than the 1:50m data is accurate), written as zig-zag varint deltas
// along each ring; latitudes are clamped to the Web Mercator limit. Rings drop their repeated closing point. The 300 largest
// populated places by pop_max are kept for labels; countries keep their label point and the data's own label zoom range; lakes
// are kept with their zoom hint so small ones can be left out when zoomed far out.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';

const [, , inputFolder, outputFile = 'src/Marrow.UI/Assets/naturalearth/world.nemap'] = process.argv;
if (!inputFolder) {
  console.error('usage: node tools/naturalearth/build.mjs <folder> [output]');
  process.exit(2);
}

const MAX_LAT = 85.0511287798;
const Q = 1000;
const bytes = [];
const u8 = (v) => bytes.push(v & 0xff);
const varint = (value) => {
  let v = value;
  while (v >= 0x80) {
    bytes.push((v & 0x7f) | 0x80);
    v = Math.floor(v / 128);
  }
  bytes.push(v);
};
const svarint = (value) => varint(value < 0 ? -value * 2 - 1 : value * 2);
const text = (value) => {
  const encoded = Buffer.from(value ?? '', 'utf8');
  varint(encoded.length);
  for (const b of encoded) bytes.push(b);
};
const quantise = (value, limit) => Math.round(Math.max(-limit, Math.min(limit, value)) * Q);
const tenth = (value) => (value == null || !Number.isFinite(value) ? 255 : Math.max(0, Math.min(254, Math.round(value * 10))));

const read = (name) => JSON.parse(readFileSync(join(inputFolder, name), 'utf8'));

const polygonsOf = (geometry) => {
  if (!geometry) return [];
  if (geometry.type === 'Polygon') return [geometry.coordinates];
  if (geometry.type === 'MultiPolygon') return geometry.coordinates;
  return [];
};

const shapes = (geometry) => {
  const polygons = polygonsOf(geometry).filter((polygon) => polygon.length > 0);
  varint(polygons.length);
  for (const polygon of polygons) {
    varint(polygon.length);
    for (const ring of polygon) {
      const open = ring.length > 1 && ring[0][0] === ring[ring.length - 1][0] && ring[0][1] === ring[ring.length - 1][1] ? ring.slice(0, -1) : ring;
      varint(open.length);
      let px = 0;
      let py = 0;
      open.forEach(([lon, lat], index) => {
        const x = quantise(lon, 180);
        const y = quantise(lat, MAX_LAT);
        svarint(index === 0 ? x : x - px);
        svarint(index === 0 ? y : y - py);
        px = x;
        py = y;
      });
    }
  }
};

for (const b of Buffer.from('NEM1', 'ascii')) bytes.push(b);
u8(1);

const countries = read('ne_50m_admin_0_countries.geojson').features.filter((f) => polygonsOf(f.geometry).length > 0);
varint(countries.length);
for (const f of countries) {
  const p = f.properties;
  text(p.NAME);
  text(p.ISO_A3 && p.ISO_A3 !== '-99' ? p.ISO_A3 : p.ADM0_A3);
  svarint(quantise(p.LABEL_X ?? 0, 180));
  svarint(quantise(p.LABEL_Y ?? 0, MAX_LAT));
  u8(Math.max(0, Math.min(254, p.LABELRANK ?? 10)));
  u8(tenth(p.MIN_LABEL));
  u8(tenth(p.MAX_LABEL));
  varint(Math.round((p.POP_EST ?? 0) / 1000));
  shapes(f.geometry);
}

const lakes = read('ne_50m_lakes.geojson').features.filter((f) => polygonsOf(f.geometry).length > 0);
varint(lakes.length);
for (const f of lakes) {
  u8(tenth(f.properties.min_zoom));
  shapes(f.geometry);
}

const places = read('ne_50m_populated_places_simple.geojson')
  .features.filter((f) => f.geometry?.type === 'Point' && (f.properties.pop_max ?? 0) > 0)
  .sort((a, b) => b.properties.pop_max - a.properties.pop_max || a.properties.name.localeCompare(b.properties.name))
  .slice(0, 300);
varint(places.length);
for (const f of places) {
  const p = f.properties;
  text(p.name);
  svarint(quantise(f.geometry.coordinates[0], 180));
  svarint(quantise(f.geometry.coordinates[1], MAX_LAT));
  varint(Math.round(p.pop_max / 1000));
  u8(p.adm0cap === 1 ? 1 : 0);
}

mkdirSync(dirname(outputFile), { recursive: true });
writeFileSync(outputFile, Buffer.from(bytes));
console.log(`${outputFile}: ${bytes.length} bytes, ${countries.length} countries, ${lakes.length} lakes, ${places.length} places`);
