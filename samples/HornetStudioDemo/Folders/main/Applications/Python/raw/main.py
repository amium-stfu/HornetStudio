# Template: Register Raw Values (3 random signals)
# Purpose:
#   Example PythonClient template that declares three raw values and updates them in a small loop.
# Notes:
#   This template sends 'define_value' and 'value_update' messages.
#   If the current host build does not yet consume these message types, they serve as protocol examples.
# Usage:
#   Start the client and observe the outgoing updates.

import random
import threading
import time
from ui_python_client import PythonClient

RUN_LOOP = False
LOOP_THREAD = None
VALUE_NAMES = ["raw_a", "raw_b", "raw_c"]
client = PythonClient("raw", capabilities=["functions", "host_log", "values"])

for value_name in VALUE_NAMES:
    client.register_value(value_name, title=f"raw/{value_name}", unit="V")


def emit_values_loop() -> None:
    while RUN_LOOP:
        client.update_value(VALUE_NAMES[0], round(random.uniform(0.0, 10.0), 3))
        client.update_value(VALUE_NAMES[1], round(random.uniform(10.0, 20.0), 3))
        client.update_value(VALUE_NAMES[2], round(random.uniform(20.0, 30.0), 3))
        time.sleep(0.5)


def start_loop() -> str:
    global RUN_LOOP, LOOP_THREAD
    if RUN_LOOP:
        return "Loop already running"

    RUN_LOOP = True
    LOOP_THREAD = threading.Thread(target=emit_values_loop, daemon=True)
    LOOP_THREAD.start()
    client.log_info("Random raw-value loop started")
    return "Loop started"


def stop_loop() -> str:
    global RUN_LOOP
    RUN_LOOP = False
    client.log_info("Random raw-value loop stopped")
    return "Loop stopped"


@client.on_init
def handle_init() -> None:
    client.log_info("Template 'Register Raw Values' initialized")


@client.on_stop
def handle_stop() -> None:
    stop_loop()


@client.function("start_loop", description="Start the random value loop.", category="values")
def start_loop_command() -> str:
    return start_loop()


@client.function("stop_loop", description="Stop the random value loop.", category="values")
def stop_loop_command() -> str:
    return stop_loop()


if __name__ == "__main__":
    raise SystemExit(client.run())
