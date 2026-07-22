namespace ValutaBot.MiniApp;

/// <summary>
/// MiniApp WebApp UI Master Container.
/// Decomposed into partial classes:
/// - MiniAppUI.Html.cs: HTML DOM structure
/// - MiniAppUI.Js.cs: Interactive client JavaScript
/// - MiniAppUI.Styles.cs: CSS design system & animations
/// </summary>
public static partial class MiniAppUI
{
    public static string GetHtml()
    {
        return $@"<!DOCTYPE html>
<html lang='ru'>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no'>
    <title>TradeBE бот — анализ рынка</title>
    <script src='https://telegram.org/js/telegram-web-app.js'></script>
    <style>
{GetCssStyles()}
    </style>
</head>
<body>
{GetHtmlBody()}
    <script>
{GetJsScript()}
    </script>
</body>
</html>";
    }
}
