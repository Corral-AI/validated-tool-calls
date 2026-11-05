# AGENTS.md

Guidance for coding agents working in this repository. CLAUDE.md imports this file, so Claude Code and Codex read the same instructions.

## Merge first, review after

This repository merges first and reviews after: whoever finishes a pull request merges it once its checks pass (`gh pr merge <number> --merge`), then checks that main still works.

- **A failure on main** is fixed forward in a new pull request. One caused by someone else's PR gets a comment on that PR.
- **Review happens after merge.** Each owner reads their own merged PRs, in any order: `corral review` lists the ones not yet read, `corral review mark <PR>` marks one read (`--asks <N>` names the PR or issue holding follow-ups, `--reverted-by <N>` the revert), and `corral review approve` pushes a `reviews/<owner>/<date>-<n>` tag up to where every one of the owner's PRs is marked.
- **A rejected PR is reverted** by a revert PR; an ask becomes a new PR or issue, never a change to the merged branch.
