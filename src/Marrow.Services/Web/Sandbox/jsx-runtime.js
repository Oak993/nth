// nth's React JSX runtime module (DESIGN.md 6.5; ED-NEW-web-w13-3): React's automatic runtime (jsx, jsxs, Fragment) over
// the vendored React, for module scripts that import "react/jsx-runtime" through the import map.
import React from "./../esm/react.js";
export const Fragment = React.Fragment;
export function jsx(type, props, key) {
  return React.createElement(type, key === undefined ? props : Object.assign({}, props, { key }));
}
export const jsxs = jsx;
export const jsxDEV = jsx;
export default { Fragment, jsx, jsxs, jsxDEV };
