# Examples

An example is one case of Jason applied to outbound work: one folder, holding the same four files every other
case holds, so that a person and an agent can compare one case with another.

## How to read one

Each case is a folder named for it, laid out like [`_template/`](_template/README.md):

| File | What it holds |
|---|---|
| `README.md` | The card: the case in one paragraph, who it was for in anonymised form, the provider and channels, the outcome in one line, whether it is `live` or `synthetic`, and the month |
| `goal.md` | The business goal, its constraints and how success was judged, and nothing else |
| `solution.md` | How it was done in Jason: the campaign plan, the roles and skills used, the operations, what a person approved and what the agents did on their own, and what was learned |
| `files/` | Attachments the solution refers to |

Read the card first, then the goal, then the solution. An agent can read `goal.md` alone to decide whether a case is
close to the one in front of it.

## The cases

| Case | Provider and channels | Outcome | Status |
|---|---|---|---|

There are no cases here yet.

A `live` case happened as it is written, with the names changed. A `synthetic` case was made up to show a pattern, and
its card says so.

## Sending a case

While Jason is in alpha this repository takes no pull requests or issues. To share a case, copy
[`_template/`](_template/README.md), fill it in, and send it to the address in the README's
[Getting help](../README.md#getting-help) section. A case that is accepted is published with a later release, credited
as you wish or not at all.

The rules for a case:

- Nobody can be identified from it: every name, address and identifier is anonymised, and the people it describes
  agreed to it being shared.
- No credential, key or token appears anywhere in its files.
- A synthetic case says so in its card.
- What you send is published under this repository's MIT licence ([LICENSE](../LICENSE)).
- Every case is read before it is published, and may be shortened or declined.
