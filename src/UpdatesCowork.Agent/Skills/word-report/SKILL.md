---
name: word-report
description: Azure の更新情報を Word の報告書にまとめる。「Wordにして」「報告書にして」「文書にまとめて」に対応する。
---
# Word の報告書を作る
1. update_plan で取得、構成、作成の手順を示す。
2. get_azure_update で対象を取得する。
3. read_skill_resource で references/outline.md を読み、構成を決める。
4. create_word_report を呼ぶ。引数は report オブジェクト1つ。report.fileName は azure-update-<ID>-report、report.language は ja-JP。出典は report.sources に入れる。sources を report の外側に置かない。各出典は取得した更新の title と公式の url を持つ。
5. 入力の検証エラーなら直して1回だけ再試行する。ほかの失敗を成功と報告しない。
6. update_plan で完了を知らせ、ファイル名と要点を短く伝える。
