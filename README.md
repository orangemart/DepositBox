## Overview

The **DepositBox** plugin allows players to deposit specific items (e.g., paper) into a dropbox, logging the deposits and removing the items from the game. This is useful for creating events or competitions where players turn in items for rewards—similar to dog tag events in Twitch Rust servers.

### Features

- Only designated items can be deposited.
- Deposits are logged and removed from the game world.
- Leaderboard-friendly: works with plugins like `UpdateLeaderboard`.
- Low memory overhead: deposit totals are no longer calculated on every deposit.
- Configurable deposit box skin, item type, and deposit limits.
- Supports automated reward distribution (e.g., Bitcoin payouts based on leaderboard rankings).

Once installed, players with permission can use `/depositbox` to receive a special deposit box. Deposits made into this box are tracked and logged to the data folder.

## Installation

1. Upload `DepositBox.cs` to your server's `/oxide/plugins/` directory.
2. Reload the plugin:

   ```bash
   oxide.reload DepositBox
   ```

3. Grant permission to users:

   ```bash
   oxide.grant user <username> depositbox.place
   ```

## Configuration

A config file is generated at `/oxide/config/DepositBox.json`:

```json
{
  "DepositBoxSkinID": 3337967028,
  "DepositItemID": -932201673,
  "MaxDepositLimit": 1000000
}
```

- `DepositItemID`: The item players can deposit (default is paper).
- `DepositBoxSkinID`: Unique skin to identify deposit boxes.
- `MaxDepositLimit`: Players exceeding this limit (based on the last leaderboard update) cannot deposit more.

> 💡 **Note:** The plugin only checks deposit limits based on the last generated summary. To update totals, run the `/depositsummary` command or let the `UpdateLeaderboard` plugin call it automatically.

## Permissions

- `depositbox.place`: Allows a player to use `/depositbox` and place a box.
- `depositbox.admincheck`: Allows a player/admin to run `/depositsummary`.

## How It Works

- Deposits are tracked and logged in `oxide/data/DepositBoxLog.json`.
- Totals and rewards are calculated **on-demand** when the summary is generated.
- Deposit limits are enforced using the last summary snapshot (`DepositBoxSummary.json`).
- This shift from live tracking to batched summarization greatly improves plugin performance.

## Summary Output

Running `/depositsummary` creates:

- `DepositBoxSummary.json`: Full deposit totals and reward percentages.
- `DepositBoxClaims.json`: Reward amounts per player.
- `DepositBoxSummary.csv`: Human-readable CSV for external analysis.

These are saved to `oxide/data/DepositBox/`.
