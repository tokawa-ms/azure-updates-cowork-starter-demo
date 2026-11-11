# Azure Updates Cowork設計キット

このzipを新しいリポジトリのルートに展開して使う。中身は次のとおり。

| パス | 内容 |
|---|---|
| `docs/design.md` | 設計書 (アーキテクチャ、各コンポーネントの仕様、データ契約、権限、観測、評価、インフラ) |
| `docs/implementation-plan.md` | 実装フェーズ (P0〜P9) と、フェーズごとのCopilotへの依頼例と受け入れ条件 |
| `.github/copilot-instructions.md` | GitHub Copilotが常に読む決まり |
| `.vscode/mcp.json` | Microsoft Learn、Azure、FoundryのMCPサーバーの設定 |
| `src/UpdatesCowork.Office/Templates/` | WordとPowerPointのテンプレート (日本語フォント設定済み、16:9) |

使い方は次のとおり。

1. 展開した内容をコミットする。
2. VS CodeのGitHub Copilot (エージェントモード) か、Copilotコーディングエージェントに「`docs/implementation-plan.md`のP0を実装して」と頼む。
3. フェーズごとにPRを確かめ、受け入れ条件を満たしたら次のフェーズに進む。

設計書の[要確認]は、2026-11-11時点で公式資料どうしが食い違う点や、プレビューで変わりやすい点を表す。実装のときに確かめ、結果をPRに書く。
