# Changelog

This package versions independently of the Jason runtime, under SemVer.

## 1.0.0

First release. Performs the delivery contract `notification.send` version 1: Jason's pointer, posted as plain text to one
Slack channel with `chat.postMessage`, with a bot token from `Plugins:Config:slack:token` or `SLACK_BOT_TOKEN`. What it does not do,
and why, is in [README.md](README.md).
