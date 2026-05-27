# Apkg

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/aiursoftweb/apkg/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/aiursoft/apkg/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/apkg/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/aiursoft/apkg/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/apkg/-/pipelines)
[![Man hours](https://manhours.aiursoft.com/r/github.com/aiursoftweb/apkg.svg)](https://manhours.aiursoft.com/r/github.com/aiursoftweb/apkg.html)
[![Website](https://img.shields.io/website?url=https%3A%2F%2Fapkg.aiursoft.com)](https://apkg.aiursoft.com)
[![Docker](https://img.shields.io/docker/pulls/aiursoft/apkg.svg)](https://hub.docker.com/r/aiursoft/apkg)

Apkg is an APT package hosting server paired with a `dotnet` global CLI tool for creating, packaging, and publishing `.apkg` files.

![screenshot](./screenshot.png)

Default user name is `admin@default.com` and default password is `Admin@123456!`.

## Install APKG CLI

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet tool install --global Aiursoft.Apkg.Client --add-source https://nuget.aiursoft.com/v3/index.json
```

After installing, verify with `apkg --help`.

### Quick workflow

```bash
# 1. Create a new package
apkg new --name my-package

# 2. Build and publish (creates .apkg in bin/)
apkg publish --path ./my-package

# 3. Push to server
apkg push ./my-package/bin/my-package.1.0.0.apkg --source https://apkg.example.com --api-key <your-key>

# 4. Add a repository as an APT source on a client machine (requires sudo)
sudo apkg add-source https://apkg.example.com/api/sources/1

# 5. Install the package via apt as normal
sudo apt install my-package
```

## Try

Try a running Apkg [here](https://apkg.aiursoft.com).

## Run in Ubuntu

The following script will install\update this app on your Ubuntu server. Supports Ubuntu 25.04.

On your Ubuntu server, run the following command:

```bash
curl -sL https://github.com/aiursoftweb/apkg/raw/master/install.sh | sudo bash
```

Of course it is suggested that append a custom port number to the command:

```bash
curl -sL https://github.com/aiursoftweb/apkg/raw/master/install.sh | sudo bash -s 8080
```

It will install the app as a systemd service, and start it automatically. Binary files will be located at `/opt/apps`. Service files will be located at `/etc/systemd/system`.

## Run manually

Requirements about how to run

1. Install [.NET 10 SDK](http://dot.net/) and [Node.js](https://nodejs.org/).
2. Execute `npm install` at `wwwroot` folder to install the dependencies.
3. Execute `dotnet run` to run the app.
4. Use your browser to view [http://localhost:5000](http://localhost:5000).

## Run in Microsoft Visual Studio

1. Open the `.sln` file in the project path.
2. Press `F5` to run the app.

## Run in Docker

First, install Docker [here](https://docs.docker.com/get-docker/).

Then run the following commands in a Linux shell:

```bash
image=aiursoft/apkg
appName=apkg
sudo docker pull $image
sudo docker run -d --name $appName --restart unless-stopped -p 5000:5000 -v /var/www/$appName:/data $image
```

That will start a web server at `http://localhost:5000` and you can test the app.

The docker image has the following context:

| Properties  | Value                           |
|-------------|---------------------------------|
| Image       | aiursoft/apkg               |
| Ports       | 5000                            |
| Binary path | /app                            |
| Data path   | /data                           |
| Config path | /data/appsettings.json          |

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.
