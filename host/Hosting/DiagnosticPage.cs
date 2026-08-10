namespace NowPlayingOverlay.Host.Hosting;

internal static class DiagnosticPage
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Now Playing protocol diagnostic</title>
          <style>
            :root{color-scheme:dark}body{font:14px system-ui;margin:24px;background:#151515;color:#eee}
            main{max-width:760px}img{width:160px;height:160px;object-fit:cover;background:#292929}
            pre{padding:16px;background:#202020;white-space:pre-wrap}button{padding:8px 12px}
          </style>
        </head>
        <body>
          <main>
            <h1>Now Playing protocol diagnostic</h1>
            <p id="connection">Connecting…</p>
            <img id="artwork" alt="Artwork unavailable" hidden>
            <pre id="state">Waiting for state…</pre>
            <button id="reload" type="button">Reload current state</button>
          </main>
          <script>
            const output=document.querySelector('#state');
            const connection=document.querySelector('#connection');
            const artwork=document.querySelector('#artwork');
            let baseline=null;
            function valid(value){return value&&value.protocolVersion===1&&typeof value.serverInstanceId==='string'&&Number.isSafeInteger(value.snapshotRevision)&&typeof value.playback==='string'&&Array.isArray(value.track?.genres??[])}
            function apply(value){
              if(!valid(value)){connection.textContent='Unsupported protocol payload';return}
              // Revisions are ordered only within one host instance.
              if(baseline&&baseline.serverInstanceId===value.serverInstanceId&&value.snapshotRevision<=baseline.snapshotRevision)return;
              baseline=value;
              output.textContent=JSON.stringify(value,null,2);
              artwork.hidden=!value.artwork;
              if(value.artwork)artwork.src=value.artwork.url;
            }
            async function load(){const response=await fetch('/api/v1/state',{cache:'no-store'});apply(await response.json())}
            document.querySelector('#reload').addEventListener('click',()=>load().catch(console.error));
            load().catch(error=>connection.textContent=`Initial state failed: ${error}`);
            const events=new EventSource('/api/v1/events');
            events.addEventListener('state',event=>{connection.textContent='SSE connected';try{apply(JSON.parse(event.data))}catch(error){connection.textContent=`Invalid SSE data: ${error}`}});
            events.onerror=()=>connection.textContent='SSE reconnecting…';
          </script>
        </body>
        </html>
        """;
}
