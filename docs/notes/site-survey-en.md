# Update site survey

[日本語 (primary)](site-survey.md) | English

Verified: 2026-11-11. The local siteproxy and web were opened in Edge. The upstream website is not a stable API contract and must be investigated again when it changes.

## Headers and origins

Web runs at `https://localhost:7101`; siteproxy runs at `https://localhost:7102`. The site could be displayed in an iframe after removing `X-Frame-Options` from the siteproxy response and adding `Content-Security-Policy: frame-ancestors 'self' https://localhost:7101`. Making the parent and child share an origin was not used as a workaround.

`bridge.js` was inserted into the HTML head; its loading and notifications to the parent were verified. ID `559242` appeared in the parent UI. Upstream `document.title` remained “Azure updates” even when displaying details, so the parent heading also showed a generic site name.

## Data API

With `ProxyDataApi=false`, these browser requests failed with `net::ERR_FAILED`, and the list showed `0 Updates found`:

- `https://www.microsoft.com/releasecommunications/api/v2/azure/559242`
- `https://www.microsoft.com/releasecommunications/api/v2/azure?$count=true&includeFacets=true&top=20&skip=0`

Because cross-origin data retrieval failed, the optional `ProxyDataApi` design feature was enabled. The bridge rewrites fetch and XMLHttpRequest calls to `/__rc/`, and YARP forwards only GET/HEAD requests to the fixed destination `www.microsoft.com/releasecommunications/`. It does not accept arbitrary URLs as arguments.

After this change, `/ja-jp/updates?id=559242` displayed one filtered result, the heading `Generally Available: Azure Container Apps Express`, and the full description. Tests also cover a 404 for the internal API path when disabled, the fixed destination and prefix when enabled, and a 405 for POST.

## Static assets and scripts

Absolute upstream URLs in HTML are rewritten as paths within the proxy. External links open in another tab. Images, styles, lists, and details displayed successfully, so no additional static-asset forwarding or integration into the parent origin was added.

Upstream scripts still produce console errors involving cross-origin frame.document and null style / __registerScript values. Security isolation was not disabled to suppress them. These results do not guarantee full compatibility with site-wide menus, sign-in, or every external asset.

## Reproduction

After starting the local services, inspect `/__rc/` responses in the browser's Network tab. Restart siteproxy with `SiteProxy__ProxyDataApi=false` to compare the failure case. Afterwards, remove the variable and restart to restore the default `true`.

The current [README](../../README-en.md) starts only Web and Agent. This survey concerns the legacy proxy configuration; start `UpdatesCowork.SiteProxy` separately when reproducing it.
