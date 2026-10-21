"""Fake external Docker boundary for script tests; not an application Docker substitute."""

import json
import os
from pathlib import Path
import sys


args = sys.argv[1:]
log = Path(os.environ["FAKE_DOCKER_LOG"])
with log.open("a") as file:
    file.write(json.dumps(args) + "\n")
state_file = Path(os.environ["FAKE_DOCKER_STATE"])
state = json.loads(state_file.read_text()) if state_file.exists() else {"containers": {}, "volumes": {}}


def save():
    state_file.write_text(json.dumps(state))


def fail():
    sys.exit(1)


if args[0] == "build":
    sys.exit(int(os.environ.get("FAKE_DOCKER_FAIL_BUILD", "0")))
if args[:2] == ["volume", "inspect"]:
    name = args[-1]
    if name not in state["volumes"]:
        fail()
    print(state["volumes"][name])
elif args[:2] == ["volume", "create"]:
    name = args[-1]
    state["volumes"][name] = next(arg.partition("=")[2] for arg in args if arg.startswith("org.worldalerts.instance="))
    save()
    print(name)
elif args[:2] == ["container", "inspect"]:
    name = args[-1]
    if name not in state["containers"]:
        fail()
    print(state["containers"][name]["label"])
elif args[:2] == ["container", "stop"]:
    state["containers"][args[-1]]["running"] = False
    save()
elif args[:2] == ["container", "rm"]:
    del state["containers"][args[-1]]
    save()
elif args[0] == "run":
    name = args[args.index("--name") + 1]
    if name in state["containers"]:
        fail()
    state["containers"][name] = {
        "label": next(arg.partition("=")[2] for arg in args if arg.startswith("org.worldalerts.instance=")),
        "running": True,
    }
    save()
    print("fake-container-id")
else:
    print("unknown fake command", args, file=sys.stderr)
    fail()
