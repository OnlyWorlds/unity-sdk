# ow-link: open `ow://` links in a running Editor or player (Windows)

A local-development handler for address links (`ow://<world id>/<type>/<id>`). When something opens such a link, the
forwarder offers it to each port in its config in turn, over 127.0.0.1, where an `OWLinkListener` in your Editor or
player may be listening. The first that answers `ok` has it. When none does, it starts your player with the link as
an argument.

| File | What it is |
|---|---|
| `ow-link.ps1` | The forwarder. Windows PowerShell, which every Windows has; nothing to install. |
| `install.ps1` | Registers the forwarder for the current user, or removes it (`-Uninstall`). |
| `ow-link.example.json` | A config to copy and edit. |

## The config

```json
{
  "ports": [47615, 47616],
  "timeout_ms": 600,
  "player": {
    "path": "%LOCALAPPDATA%\\Programs\\MyWorldPlayer\\MyWorldPlayer.exe",
    "args": ["--ow-link", "{link}"]
  }
}
```

- `ports`: tried in order, typically your Editor's listener first, then your player's. Use the ports your own code
  passes to `OWLinkListener`.
- `timeout_ms`: how long each port may take to answer (default 600).
- `player` (optional): what to start when no port takes the link. `path` may hold environment variables; each of
  `args` is one argument, with `{link}` replaced by the link. Without a player, an untaken link is dropped.

The installed config lives at `%LOCALAPPDATA%\OnlyWorlds\ow-link\ow-link.json`; edit it there at any time. The
forwarder reads it fresh on every link.

## Install and uninstall

```
powershell -ExecutionPolicy Bypass -File install.ps1 -Config my-ow-link.json
powershell -ExecutionPolicy Bypass -File "%LOCALAPPDATA%\OnlyWorlds\ow-link\install.ps1" -Uninstall
```

The installer copies the forwarder, itself and your config to `%LOCALAPPDATA%\OnlyWorlds\ow-link` (so the handler
survives a package update or removal) and writes `HKCU\Software\Classes\ow` with `URL Protocol` and a
`shell\open\command` that runs the forwarder with a hidden window. It never writes `HKLM` and never asks for an
administrator. It refuses to replace an `ow:` handler that is not its own unless you pass `-Force`. `-DryRun` prints
what it would do and changes nothing. Uninstall removes the key, only when it is this forwarder's, and the folder.

## What it will and won't pass on

- Only `ow:` links, at most 4096 characters, made of the characters an address can hold. A link with a quote, a
  space or a backslash is refused before anything is sent or started, so it can never reach a command line.
- The config is never taken from an argument: an argument smuggled in beside the link cannot choose another config
  or another player.
- A listener that reads the link and refuses it (`refused unknown_type`, say) ends the attempt: no other listener and
  no player would read it either. A listener with nobody to take it (`refused no_receiver`) and a port where nothing
  answers both pass the link on to the next.

The window is hidden, so the outcome of the last link goes to `last-run.txt` beside the config. Exit codes: 0
delivered, 1 the player started, 2 the link was refused, 3 nowhere to deliver it, 4 the config is unusable.

## The protocol

To write a listener or a sender of your own: connect to `127.0.0.1:<port>`, write the link as UTF-8 and `\n` (at
most 4096 bytes before it), read one line back: `ok`, or `refused <reason>`. The listener closes the connection after
answering. `OWLinkListener.TrySend` is the C# sender.
