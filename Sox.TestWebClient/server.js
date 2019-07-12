const http = require('http');
const port = process.env.PORT || 1337;
const fs = require('fs');

http.createServer(function (req, res) {

  console.log(`${req.method} ${req.url}`);

  if (req.url === '/') {
    res.writeHead(200, { 'Content-Type': 'text/html' });
    fs.readFile('index.html', (err, data) => {
      res.end(data);
    });
  } else {
    res.writeHead(200);
    var filePath = req.url.slice(1, req.url.length);
    fs.readFile(filePath, (err, data) => {
      res.end(data);
    });
  }


}).listen(port);
