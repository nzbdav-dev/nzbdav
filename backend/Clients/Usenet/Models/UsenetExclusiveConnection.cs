using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet.Models;

public readonly struct UsenetExclusiveConnection(Action<ArticleBodyResult>? onConnectionReadyAgain)
{
    public Action<ArticleBodyResult>? OnConnectionReadyAgain => onConnectionReadyAgain;
}