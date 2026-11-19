# Your solution as the server will run it. Check it first:  ushqyr-bench submit-check .
FROM ushqyr-bench-base:cpu
COPY requirements.txt .
RUN pip install -r requirements.txt
COPY . .
# The container has no network except the simulator, a read-only file system (write to /tmp only) and no root.
CMD ["python", "solution.py"]
