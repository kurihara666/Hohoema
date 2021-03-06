# Hohoema

Hohoemaはオープンソースな Windows 10向けのニコニコ動画プレイヤーです。

一般会員やモバイル端末にも対応しています。

## ダウンロード

Windows 10 のストアアプリから無料でダウンロードできます

**[Hohoema - アプリストア](https://www.microsoft.com/ja-jp/store/p/hohoema/9nblggh4rxt6)**
（※ Windows10でアクセスするとストアアプリが起動します）


**重要：Hohoemaのストアアプリは無料評価版が無料で無期限にご利用いただけます。**

ストアから購入するとHohoemaや開発者（tor4kichi）を支援することができます。
使えるニコ動プレイヤーにしていきますのでご支援よろしくお願いします。

## 主な機能

* ニコニコ動画の動画再生
* コメントの投稿
* ランキングや検索から動画を見つける
* 動画NG設定（動画タイトル/投稿者ID）
* お気に入りの管理
* マイリストの管理 (v0.3.0)
* キャッシュの管理（v0.6で安定性改善）
* 動画フィード機能
  * 「マイリストの動画」「タグ検索結果」「ユーザーの投稿動画」をそれぞれ時系列で並べて新着表示できる機能
* ニコニコ生放送の視聴 (v0.5.0) 
* プレイリスト（v0.6.0）
* XboxOneとXInput対応（v0.6.0）
* 再生速度の変更に対応（v0.7）
 

その他の機能や修正点は [リリースノート](https://github.com/tor4kichi/Hohoema/wiki/%E3%83%AA%E3%83%AA%E3%83%BC%E3%82%B9%E3%83%8E%E3%83%BC%E3%83%88) から確認できます



## 今後の予定

* **[v0.8 XboxOne画面の改善](https://github.com/tor4kichi/Hohoema/milestone/15)**
* [v0.9 正規表現NG対応](https://github.com/tor4kichi/Hohoema/milestone/14)
* [v0.10 プレイヤーウィンドウ分離（CompactOverlay対応）](https://github.com/tor4kichi/Hohoema/milestone/16)
* [v0.11 生放送開始検出やフィード自動更新などのBG処理対応](https://github.com/tor4kichi/Hohoema/milestone/4)


## 要望・バグ報告について

Hohoemaへの要望やバグ報告は下記のいずれかの方法でご連絡ください

* フィードバックHub: （区分：アプリとゲーム、 カテゴリ：Hohoema）
* github: [新しいIssueを立てる](https://github.com/tor4kichi/Hohoema/issues)
* twitter: [@tor4kichi](https://twitter.com/tor4kichi)
* mail: tor4kichi@hotmail.com

特にMobileやXboxOne、コントローラー操作での不具合や改善点を頂けると助かります。

今後の変更や確認されているバグは [イシュー](https://github.com/tor4kichi/Hohoema/issues) から確認できます


## オープンソースの理由は？

アプリの制作が打ち切られた場合でも有志の方が何かしら引き継いだアプリを作れるようにするためです。

また、信用の無さをコード公開による透明性で補う目的もあります。

## ライセンス

GPL v3


## 制作環境

* VisualStudio 2017 Community
* UWP (Xaml + C#)
* Inkscape
