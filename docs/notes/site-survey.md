# 更新情報サイトの調査

日本語 (プライマリ) | [English](site-survey-en.md)

確認日: 2026-11-11。Edge のブラウザーでローカル siteproxy と web を開いて確認した。上流サイトの仕様は固定APIではなく、変更時には再調査が必要になる。

## ヘッダーとオリジン

web は `https://localhost:7101`、siteproxy は `https://localhost:7102`。siteproxy の応答から `X-Frame-Options` を除去し、`Content-Security-Policy: frame-ancestors 'self' https://localhost:7101` を付けた状態で iframe 表示できた。親子を同一オリジンにする回避策は採用していない。

`bridge.js` は HTML の head に挿入され、読み込みと親への通知を確認した。ID `559242` が親画面に反映された。上流の `document.title` は詳細表示でも「Azure の更新情報」のままであり、親の見出しも一般的なサイト名になる。

## データAPI

`ProxyDataApi=false` では次のブラウザー要求が `net::ERR_FAILED` になり、一覧は `0 Updates found` だった。

- `https://www.microsoft.com/releasecommunications/api/v2/azure/559242`
- `https://www.microsoft.com/releasecommunications/api/v2/azure?$count=true&includeFacets=true&top=20&skip=0`

別オリジンへのデータ取得が失敗するため、設計の任意機能 `ProxyDataApi` を有効にした。bridge が fetch と XMLHttpRequest を `/__rc/` に読み替え、YARP が固定された `www.microsoft.com/releasecommunications/` に GET/HEAD だけを中継する。任意URLを引数として受け取る仕組みではない。

変更後、`/ja-jp/updates?id=559242` でフィルター結果1件、`Generally Available: Azure Container Apps Express` の見出しと詳細本文を確認した。無効設定で内部APIパスが404になるテスト、有効設定で宛先・prefixを固定するテスト、POSTを405にするテストも保持している。

## 静的資産とスクリプト

HTML内の上流絶対URLをプロキシ内のパスに読み替える。外部リンクは別タブにする。ページの画像、スタイル、一覧・詳細は表示できたため、静的資産の追加中継や親オリジンへの統合は行っていない。

上流スクリプトには cross-origin frame.document、null の style／__registerScript に関するコンソールエラーが残る。セキュリティ分離を無効にして消す対応はしていない。サイト全体のメニュー、サインイン、すべての外部資産の完全な互換性を保証する結果ではない。

## 再現手順

READMEのローカル起動後、ブラウザーのネットワークタブで `/__rc/` の応答を確認する。`SiteProxy__ProxyDataApi=false` にして siteproxy を再起動すると失敗時と比較できる。比較後は変数を削除して再起動し、既定の `true` に戻す。

現在の[README](../../README.md)ではWebとAgentだけを起動する。この調査は旧プロキシ構成の記録なので、再現するときは`UpdatesCowork.SiteProxy`を別途起動する。
