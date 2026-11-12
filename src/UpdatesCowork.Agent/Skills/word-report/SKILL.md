---
name: word-report
description: Azure の更新情報を Word の報告書にまとめる。「Wordにして」「報告書にして」「文書にまとめて」に対応する。
---
# Word の報告書を作る
対象 ID がページ情報にも会話にもなく、検索条件も指定されていない場合は、更新の選択または ID を依頼して終了する。仮の ID で取得しない。以下は対象が確定した後の手順。

1. update_plan で取得、構成、作成の手順を示す。
2. get_azure_update で対象を取得する。userNotice があれば最終回答にも通知する。本文が遮断された場合は内容を推測して資料にしない。
3. read_skill_resource で references/outline.md を読み、構成を決める。利用者が表にする対象や必須の項目を指定した場合は、その指定を優先する。たとえば「対応範囲を表で」の依頼を、提供時期の表で代用してはいけない。
4. create_word_report を呼ぶ。引数は report オブジェクト1つ。report.fileName は azure-update-<ID>-report、report.language は ja-JP。出典は report.sources に入れる。sources を report の外側に置かない。各出典は取得した更新の title と公式の url を持つ。
5. 入力の検証エラーなら直して1回だけ再試行する。ほかの失敗を成功と報告しない。
6. update_plan で完了を知らせ、ファイル名と要点を日本語で短く伝える。その後に、更新 ID と公式 URL を回答にも必ず書く。ファイル内に出典があっても回答の出典は省略しない。公式記事へのリンクは必要であり、作ってはいけないのはファイルのダウンロードリンク。
