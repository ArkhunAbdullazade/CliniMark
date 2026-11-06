// Local-only fixture server. Never serves configuration, source C#, or directory listings.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
http.createServer((req, res) => {
    const url = new URL(req.url, 'http://127.0.0.1:5211');
    const allowed = req.method === 'GET' && (
        /^\/tests\/browser\/[a-zA-Z0-9_-]+\.(html|svg)$/.test(url.pathname) ||
        /^\/src\/CliniMark.Presentation\/wwwroot\/js\/[a-zA-Z0-9_-]+\.js$/.test(url.pathname) ||
        /^\/images\/[a-zA-Z0-9_-]+\.(png|svg)$/.test(url.pathname) ||
        /^\/src\/CliniMark.Presentation\/wwwroot\/images\/[a-zA-Z0-9_-]+\.(png|svg)$/.test(url.pathname));
    if (!allowed) { res.writeHead(404); res.end(); return; }
    const relative = url.pathname.startsWith('/images/')
        ? 'src/CliniMark.Presentation/wwwroot' + url.pathname : url.pathname.slice(1);
    const file = path.join(root, relative);
    fs.readFile(file, (error, content) => {
        if (error) { res.writeHead(404); res.end(); return; }
        const type = {'.html': 'text/html', '.svg': 'image/svg+xml', '.js': 'text/javascript', '.png': 'image/png'}[path.extname(file)];
        res.writeHead(200, {'Content-Type': type, 'Cache-Control': 'no-store'});
        res.end(content);
    });
}).listen(5211, '127.0.0.1', () => console.log('Coordinate fixtures: http://127.0.0.1:5211/tests/browser/coordinates.html'));
