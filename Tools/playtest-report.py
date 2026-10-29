#!/usr/bin/env python3
"""Analysis of the PlaytestLogger CSVs (card C0.4).

Usage: python Tools/playtest-report.py <folder with playtest_*.csv> [--png-dir <folder>]

Reads every playtest_*.csv in the folder (one file per match and per machine) and prints to the console:
win rate by role, the objective funnel with the time of each milestone, kicks landed and missed, freezes per
minute and mean duration, idle time per player and logs dropped by distance. If matplotlib is installed it also
saves one position heat map per role as a PNG; if not, that step is skipped. The Usability section (card C3.3) shows
the median time to first interaction, the share of players who finish their first objective in 45 s and the
"you can't" prompts per minute; the Survey section aggregates the post-match survey rows.

Only the standard library (csv, statistics) is required. Row format, written by Telemetry.cs:
    matchId,t,event,playerId,role,x,z,phase,data      with data = key=value;key=value
matchId is "<session>_<start tick>". Each machine writes its own file, so the same match shows up in several
files and the start ticks can differ by a tick or two between machines: ids of one session whose tick differs by
at most TICK_TOLERANCE are merged. State events (pickup, fire_lit, ...) come only from the master's file;
input events (position_sample, idle, kick, ...) from the file of the player who did them.
"""
import csv
import glob
import os
import statistics
import sys
from collections import defaultdict

TICK_TOLERANCE = 5
FIRST_GOAL_SECONDS = 45.0  # usability target: first objective completed within this time (card C3.3)
FUNNEL_LOGS = 5  # logs needed to build the fire (FireplaceManager Required Wood)


def parse_data(text):
    """Turns 'a=1;b=x' into {'a': '1', 'b': 'x'}."""
    result = {}
    for part in text.split(";"):
        if "=" in part:
            key, value = part.split("=", 1)
            result[key] = value
    return result


def to_float(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


class Event:
    __slots__ = ("match", "t", "name", "player", "role", "x", "z", "phase", "data", "source")

    def __init__(self, row, source):
        self.match = row[0]
        self.t = to_float(row[1])
        self.name = row[2]
        self.player = row[3]
        self.role = row[4]
        self.x = to_float(row[5])
        self.z = to_float(row[6])
        self.phase = row[7]
        self.data = parse_data(row[8]) if len(row) > 8 else {}
        self.source = source


def load(folder):
    events = []
    files = sorted(glob.glob(os.path.join(folder, "playtest_*.csv")))
    for path in files:
        with open(path, newline="", encoding="utf-8") as handle:
            reader = csv.reader(handle)
            header = next(reader, None)
            if not header or header[0] != "matchId":
                print("Skipping {} (unexpected header)".format(os.path.basename(path)))
                continue
            for row in reader:
                if len(row) >= 8:
                    events.append(Event(row, os.path.basename(path)))
    return events, len(files)


def split_match_id(match_id):
    session, _, tick = match_id.rpartition("_")
    try:
        return session, int(tick)
    except ValueError:
        return match_id, 0


def merge_match_ids(events):
    """Maps every raw matchId to one canonical id per real match (same session, start ticks within tolerance)."""
    ids = sorted({e.match for e in events}, key=split_match_id)
    canonical = {}
    current = None
    for match_id in ids:
        session, tick = split_match_id(match_id)
        if current and current[0] == session and tick - current[1] <= TICK_TOLERANCE:
            canonical[match_id] = current[2]
            current = (session, tick, current[2])
        else:
            canonical[match_id] = match_id
            current = (session, tick, match_id)
    return canonical


def dedupe(events):
    """The same stage change is logged by every machine; keep one per (match, stage, second)."""
    seen = set()
    result = []
    for e in sorted(events, key=lambda e: e.t):
        if e.name == "stage_change":
            key = (e.match, e.data.get("stage"), round(e.t))
            if key in seen:
                continue
            seen.add(key)
        result.append(e)
    return result


def fmt_time(seconds):
    return "{:d}:{:02d}".format(int(seconds // 60), int(seconds % 60))


def summarize(values):
    if not values:
        return "-"
    return "median {} (min {}, max {}, n={})".format(
        fmt_time(statistics.median(values)), fmt_time(min(values)), fmt_time(max(values)), len(values))


def section(title):
    print()
    print(title)
    print("-" * len(title))


def report_overview(by_match):
    section("Matches")
    durations = []
    for match, events in sorted(by_match.items()):
        end = next((e for e in events if e.name == "match_end"), None)
        start = next((e for e in events if e.name == "match_start"), None)
        players = start.data.get("players", "?") if start else "?"
        if end:
            durations.append(to_float(end.data.get("duration")))
            print("  {}: {} players, {} ({}), {}".format(
                match, players, end.data.get("result", "?"), end.data.get("cause", "?"),
                fmt_time(to_float(end.data.get("duration")))))
        else:
            print("  {}: {} players, no match_end (incomplete)".format(match, players))
    return durations


def report_win_rate(by_match):
    section("Win rate by role")
    camper = gnomes = 0
    for events in by_match.values():
        end = next((e for e in events if e.name == "match_end"), None)
        if not end:
            continue
        if end.data.get("result") == "camper_wins":
            camper += 1
        elif end.data.get("result") == "gnomes_win":
            gnomes += 1
    total = camper + gnomes
    if total == 0:
        print("  No finished matches.")
        return
    print("  Camper: {}/{} ({:.0f}%)".format(camper, total, 100.0 * camper / total))
    print("  Gnomes: {}/{} ({:.0f}%)".format(gnomes, total, 100.0 * gnomes / total))


def first_time(events, predicate):
    times = [e.t for e in events if predicate(e)]
    return min(times) if times else None


def report_funnel(by_match):
    section("Objective funnel (time of each milestone, from match start)")
    milestones = []
    for k in range(1, FUNNEL_LOGS + 1):
        milestones.append(("log {} placed".format(k),
                           lambda e, k=k: e.name == "log_in_zone" and int(to_float(e.data.get("count"))) >= k))
    milestones += [
        ("matches picked up", lambda e: e.name == "pickup" and "sforos" in e.data.get("object", "")),
        ("fire lit", lambda e: e.name == "fire_lit"),
        ("stage: defend", lambda e: e.name == "stage_change" and e.data.get("stage") == "defend"),
        ("sausage on grill", lambda e: e.name == "sausage_state" and e.data.get("on_grill") == "1"),
        ("sausage cooked", lambda e: e.name == "sausage_state" and e.data.get("state") == "cooked"),
        ("sausage eaten", lambda e: e.name == "sausage_eaten"),
    ]
    for label, predicate in milestones:
        times = []
        for events in by_match.values():
            moment = first_time(events, predicate)
            if moment is not None:
                times.append(moment)
        print("  {:<18} reached in {}/{} matches, {}".format(label, len(times), len(by_match), summarize(times)))

    # Time from the sausage first on the grill to the end state, from the event times (cook_s only counts lit-fire time).
    for label, name in (("eaten", "sausage_eaten"), ("burnt", "sausage_burnt")):
        spans = []
        for events in by_match.values():
            on = first_time(events, lambda e: e.name == "sausage_state" and e.data.get("on_grill") == "1")
            end = first_time(events, lambda e, name=name: e.name == name)
            if on is not None and end is not None and end >= on:
                spans.append(end - on)
        print("  grill -> {:<6} {}".format(label, summarize(spans)))

    section("Fire out / pours")
    outs = sum(1 for events in by_match.values() for e in events if e.name == "fire_out")
    fills = sum(1 for events in by_match.values() for e in events if e.name == "bucket_fill")
    doused = sum(1 for events in by_match.values() for e in events
                 if e.name == "pour_end" and e.data.get("result") == "doused")
    reasons = defaultdict(int)
    for events in by_match.values():
        for e in events:
            if e.name == "pour_end" and e.data.get("result") == "cancelled":
                reasons[e.data.get("reason", "?")] += 1
    print("  Bucket fills: {}, pours that put the fire out: {} (fire_out events: {})".format(fills, doused, outs))
    if reasons:
        print("  Cancelled pours by reason: " + ", ".join("{} x{}".format(k, v) for k, v in sorted(reasons.items())))
    else:
        print("  Cancelled pours: none")


def report_kicks(by_match):
    section("Kicks")
    hits = misses = 0
    distances = []
    for events in by_match.values():
        for e in events:
            if e.name != "kick":
                continue
            if e.data.get("hit") == "1":
                hits += 1
                distances.append(to_float(e.data.get("distance")))
            else:
                misses += 1
    total = hits + misses
    if total == 0:
        print("  No kicks.")
        return
    print("  Landed {}, missed {} ({:.0f}% accuracy)".format(hits, misses, 100.0 * hits / total))
    if distances:
        print("  Mean distance of landed kicks: {:.2f} m".format(statistics.mean(distances)))
    stuns = [to_float(e.data.get("duration")) for events in by_match.values() for e in events if e.name == "stun_end"]
    if stuns:
        print("  Stuns: {}, mean duration {:.2f} s".format(len(stuns), statistics.mean(stuns)))


def report_freezes(by_match):
    section("Freezes")
    # Only finished matches count, so the per-minute rate uses the same matches as its numerator.
    counted = [events for events in by_match.values() if any(e.name == "match_end" for e in events)]
    minutes = sum(to_float(e.data.get("duration")) for events in counted for e in events if e.name == "match_end") / 60.0
    starts = sum(1 for events in counted for e in events if e.name == "freeze_start")
    escapes = sum(1 for events in counted for e in events
                  if e.name == "freeze_escape" and e.data.get("how") == "lost_sight")
    lengths = [to_float(e.data.get("duration")) for events in counted for e in events if e.name == "freeze_end"]
    if starts == 0 and escapes == 0:
        print("  No freezes.")
        return
    rate = "{:.2f}".format(starts / minutes) if minutes > 0 else "n/a"
    print("  Freezes: {} ({} per minute of play, {} finished matches)".format(starts, rate, len(counted)))
    attempts = starts + escapes
    if attempts:
        print("  Escape rate (lost_sight / (lost_sight + freezes)): {}/{} = {:.0f}% (target 20-40%)".format(
            escapes, attempts, 100.0 * escapes / attempts))
    if lengths:
        print("  Mean time frozen: {:.1f} s (median {:.1f}, n={})".format(
            statistics.mean(lengths), statistics.median(lengths), len(lengths)))
    hows = defaultdict(int)
    for events in counted:
        for e in events:
            if e.name == "freeze_end":
                hows[e.data.get("how", "?")] += 1
    if hows:
        print("  Ended by: " + ", ".join("{} x{}".format(k, v) for k, v in sorted(hows.items())))


def report_idle(by_match):
    section("Idle time per player (more than 5 s without moving or interacting)")
    totals = defaultdict(float)
    for match, events in by_match.items():
        for e in events:
            if e.name == "idle":
                totals[(match, e.player, e.role)] += to_float(e.data.get("duration"))
    if not totals:
        print("  No idle spells.")
        return
    for (match, player, role), seconds in sorted(totals.items()):
        print("  {} player {} ({}): {:.0f} s".format(match, player, role, seconds))


def report_drops(by_match):
    section("Drops")
    by_reason = defaultdict(int)
    logs_by_distance = 0
    for events in by_match.values():
        for e in events:
            if e.name != "drop":
                continue
            by_reason[e.data.get("reason", "?")] += 1
            if e.data.get("reason") == "distance" and e.data.get("object") == "leño":
                logs_by_distance += 1
    if not by_reason:
        print("  No drops.")
        return
    print("  By reason: " + ", ".join("{} x{}".format(k, v) for k, v in sorted(by_reason.items())))
    print("  Logs dropped by distance: {}".format(logs_by_distance))


def report_voice(by_match):
    section("Voice")
    durations = [to_float(e.data.get("duration")) for events in by_match.values() for e in events if e.name == "voice_active"]
    if not durations:
        print("  No voice activity.")
        return
    print("  Talking spells: {}, total {:.0f} s, mean {:.1f} s".format(len(durations), sum(durations), statistics.mean(durations)))


def match_minutes(events):
    """Duration of a match in minutes: match_end duration, else the last event time."""
    end = next((e for e in events if e.name == "match_end"), None)
    seconds = to_float(end.data.get("duration")) if end else max((e.t for e in events), default=0.0)
    return seconds / 60.0


def report_usability(by_match):
    section("Usability (targets: first interaction < 15 s, >= 80% first goal in 45 s, < 2 denied prompts/min)")
    # Players known per match: every actor id with a role (camper/gnome) on any event.
    players = {}
    for match, events in by_match.items():
        for e in events:
            if e.player not in ("", "-1") and e.role in ("camper", "gnome"):
                players.setdefault((match, e.player), e.role)
    if not players:
        print("  No player events.")
        return

    firsts = []
    for (match, player) in players:
        times = [e.t for e in by_match[match] if e.name == "first_interaction" and e.player == player]
        if times:
            firsts.append(min(times))
    if firsts:
        print("  First interaction: median {:.1f} s (min {:.1f}, max {:.1f}, n={})".format(
            statistics.median(firsts), min(firsts), max(firsts), len(firsts)))
    else:
        print("  First interaction: no data.")

    for role, label, goal in (("camper", "first log placed", lambda e: e.name == "log_in_zone"),
                              ("gnome", "first pickup/hide", lambda e: e.name in ("pickup", "hide"))):
        total = done = 0
        for (match, player), player_role in players.items():
            if player_role != role:
                continue
            total += 1
            times = [e.t for e in by_match[match] if e.player == player and e.role == role and goal(e)]
            if times and min(times) <= FIRST_GOAL_SECONDS:
                done += 1
        if total:
            print("  {:<6} {} within {:.0f} s: {}/{} ({:.0f}%)".format(role, label, FIRST_GOAL_SECONDS, done, total, 100.0 * done / total))

    denied = sum(1 for events in by_match.values() for e in events if e.name == "prompt_denied")
    minutes = 0.0
    for match, _player in players:
        minutes += match_minutes(by_match[match])
    if minutes > 0:
        print("  Denied prompts: {} over {:.1f} player-minutes = {:.2f} per minute".format(denied, minutes, denied / minutes))
    else:
        print("  Denied prompts: {} (no duration to normalise)".format(denied))


def report_survey(by_match):
    section("Survey")
    rows = [e for events in by_match.values() for e in events if e.name == "survey"]
    if not rows:
        print("  No survey answers.")
        return
    print("  {} answers".format(len(rows)))
    funs = [to_float(e.data["fun"]) for e in rows if "fun" in e.data]
    if funs:
        print("  Fun (1-5): mean {:.2f} (n={})".format(statistics.mean(funs), len(funs)))
    for role in ("camper", "gnome"):
        answers = [e.data["dizzy"] == "1" for e in rows if e.role == role and "dizzy" in e.data]
        if answers:
            print("  Dizzy as {}: {}/{} ({:.0f}%)".format(role, sum(answers), len(answers), 100.0 * sum(answers) / len(answers)))
    understood = [e.data["understood"] == "1" for e in rows if "understood" in e.data]
    if understood:
        print("  Understood the objective: {}/{} ({:.0f}%, target >= 85%)".format(
            sum(understood), len(understood), 100.0 * sum(understood) / len(understood)))
    moments = [(e.role, e.data["moment"]) for e in rows if e.data.get("moment")]
    if moments:
        print("  Memorable moments:")
        for role, text in moments:
            print("    [{}] {}".format(role, text))


def save_heatmaps(by_match, out_dir):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        print()
        print("matplotlib is not installed: heat maps skipped.")
        return

    points = defaultdict(lambda: ([], []))
    for events in by_match.values():
        for e in events:
            if e.name == "position_sample":
                points[e.role][0].append(e.x)
                points[e.role][1].append(e.z)

    section("Heat maps")
    if not points:
        print("  No position samples.")
        return
    for role, (xs, zs) in sorted(points.items()):
        figure, axis = plt.subplots(figsize=(6, 6))
        axis.hist2d(xs, zs, bins=40, cmap="inferno")
        axis.set_title("Positions: {} ({} samples)".format(role, len(xs)))
        axis.set_xlabel("x (m)")
        axis.set_ylabel("z (m)")
        axis.set_aspect("equal")
        path = os.path.join(out_dir, "heatmap_{}.png".format(role))
        figure.savefig(path, dpi=120)
        plt.close(figure)
        print("  Saved " + path)


def main(argv):
    if len(argv) < 2 or argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0 if len(argv) >= 2 else 1

    folder = argv[1]
    out_dir = folder
    if "--png-dir" in argv:
        index = argv.index("--png-dir")
        if index + 1 < len(argv):
            out_dir = argv[index + 1]

    events, file_count = load(folder)
    if not events:
        print("No playtest_*.csv with events in {}".format(folder))
        return 1

    canonical = merge_match_ids(events)
    for e in events:
        e.match = canonical[e.match]
    events = dedupe(events)

    by_match = defaultdict(list)
    for e in events:
        by_match[e.match].append(e)

    print("{} files, {} events, {} matches".format(file_count, len(events), len(by_match)))
    durations = report_overview(by_match)
    report_win_rate(by_match)
    report_funnel(by_match)
    report_kicks(by_match)
    report_freezes(by_match)
    report_idle(by_match)
    report_drops(by_match)
    report_voice(by_match)
    report_usability(by_match)
    report_survey(by_match)
    save_heatmaps(by_match, out_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
