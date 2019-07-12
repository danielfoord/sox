# Sox
| Branch | Pipeline | Coverage |
|---|:---:|:---:|
| Master | [![pipeline status](https://gitlab.com/danielfoord/sox/badges/master/pipeline.svg)](https://gitlab.com/danielfoord/sox/commits/master) | [![coverage report](https://gitlab.com/danielfoord/sox/badges/master/coverage.svg)](https://gitlab.com/danielfoord/sox/commits/master) |
| Develop | [![pipeline status](https://gitlab.com/danielfoord/sox/badges/develop/pipeline.svg)](https://gitlab.com/danielfoord/sox/commits/develop) | [![coverage report](https://gitlab.com/danielfoord/sox/badges/develop/coverage.svg)](https://gitlab.com/danielfoord/sox/commits/develop) |

A pure websocket implementation for .NET Core

## Testing on your local machine

If you just want to run the tests:

`dotnet test`

If you want full coverage:

First install the coverage report generator:

`dotnet tool install -g dotnet-reportgenerator-globaltool`

Then run the test script which will run the tests and generate the coverage report:

`./test.sh`


## Get SSL (WSS) working on your local machine

The following instructions are for Debian/Linux:

First generate your CA key:

`openssl genrsa -des3 -out rootCA.key 2048`

Generate your root CA SSL certificate:

`openssl req -x509 -new -nodes -key rootCA.key -sha256 -days 1024 -out rootCA.pem`

`sudo mkdir /usr/local/share/ca-certificates/extra`

`sudo cp rootCA.pem /usr/local/share/ca-certificates/extra/rootCA.crt`

`sudo update-ca-certificates` or `sudo dpkg-reconfigure ca-certificates`

Create a private key:

```
openssl req \
 -new -sha256 -nodes \
 -out localhost.csr \
 -newkey rsa:2048 -keyout localhost.key \
 -subj "/C=IN/ST=State/L=City/O=Organization/OU=OrganizationUnit/CN=demo/emailAddress=demo@example.com"
```

Create X509 V3 file called v3.ext:
```
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
```

Generate your certificate signing request
```
openssl x509 \
 -req \
 -in localhost.csr \
 -CA rootCA.pem -CAkey rootCA.key -CAcreateserial \
 -out localhost.crt \
 -days 500 \
 -sha256 \
 -extfile v3.ext
```


Create a signed certificate with your key:
```
openssl pkcs12 -export -out sox.pfx -inkey localhost.key -in localhost.crt
```

If the above is still not working, add rootCA.pem to the CA's in your browser.


