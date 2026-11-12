---
name: slide-deck
description: Azure の更新情報を PowerPoint のスライドにする。「PowerPointにして」「スライドにして」「PPTにして」に対応する。
---
# スライドを作る
1. update_plan で取得、構成、作成を示す。
2. get_azure_update で対象を取得する。
3. 枚数指定がなければ本文は4枚以内、全体は6枚以内にする。表紙と出典はツールが2枚追加する。「3枚の資料」のような指定は表紙と出典を含む合計なので、deck.slides は指定枚数から2枚引く。「本文3枚」と明示された場合だけ deck.slides を3枚にする。合計3枚未満はこのテンプレートでは作れないため確認する。
4. 各スライドに短い見出し、最大6つの箇条書き、話す内容の notes を入れる。箇条書きは1項目60文字以内を目安にする。
5. create_powerpoint_deck を呼ぶ。引数は deck オブジェクトと totalSlides (資料全体の枚数)。たとえば「3枚の資料」なら totalSlides は3、deck.slides は本文1枚にする。利用者の指定枚数を変えて検証を回避してはいけない。deck.fileName は azure-update-<ID>-deck。出典は deck.sources に入れ、各出典に title と公式の url を指定する。sources を deck の外側に置かない。
6. update_plan で完了を知らせ、ファイル名と要点を伝える。枚数を報告するときは戻り値の artifact.itemCount (表紙・出典込みの実際の総枚数)を使い、本文の枚数と取り違えない。取得した内容にないことは書かない。
