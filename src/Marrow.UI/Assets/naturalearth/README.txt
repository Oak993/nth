world.nemap - the offline basemap of nth-ui's `map` (DESIGN.md 7.2, W16c).

Made by tools/naturalearth/build.mjs from three Natural Earth 1:50m vector layers:
  ne_50m_admin_0_countries, ne_50m_lakes, ne_50m_populated_places_simple
Natural Earth (https://www.naturalearthdata.com) puts its maps in the public domain: they may be used, changed and shipped for
any purpose with no permission and no credit. nth credits it anyway in THIRD_PARTY_NOTICES.md.

The file holds the countries' outlines (with each country's name, ISO code, label point and label zoom range), the lakes, and
the 300 largest populated places by population. Coordinates are quantised to 0.001 degree. The format is described at the top of
tools/naturalearth/build.mjs and read by Marrow.UI/NthUi/Map/NthUiMapData.cs.
Rebuild: node tools/naturalearth/build.mjs <folder holding the three GeoJSON files>
