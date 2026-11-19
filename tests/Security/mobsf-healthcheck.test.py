"""Exercise the actual Compose health probe with fake HTTP; no containers or real keys."""
import io
import os
from pathlib import Path
import re
import textwrap
import urllib.error
from unittest.mock import patch

compose = Path("deploy/security/compose.yaml").read_text()
probe = textwrap.dedent(re.search(r"        - \|\n(.*?)      interval:", compose, re.S).group(1))


class Response(io.BytesIO):
    status = 200


for body, status, expected in [
    (b'{"content":[]}', 200, 0),
    (b'{"error":"failed"}', 200, 1),
    (b'<html>Login</html>', 200, 1),
    (b'[]', 200, 1),
    (b'{}', 401, 1),
    (b'{}', 500, 1),
]:
    def fake_open(request, timeout):
        assert request.full_url == "http://127.0.0.1:8000/api/v1/scans"
        assert request.get_header("Authorization") == "fixture-api-key"
        assert timeout == 10
        if status != 200:
            raise urllib.error.HTTPError(request.full_url, status, "fixture", {}, None)
        return Response(body)

    with patch.dict(os.environ, {"MOBSF_API_KEY": "fixture-api-key"}), patch("urllib.request.urlopen", fake_open):
        try:
            exec(compile(probe, "compose-healthcheck", "exec"), {})
        except SystemExit as result:
            assert result.code == expected, (body, status, result.code)
        else:
            raise AssertionError("Health probe did not return an exit code")

assert "condition: service_healthy" in compose
assert "MOBSF_API_ONLY: '1'" in compose
print("PASS actual Compose MobSF health probe: authenticated API, failure handling, API-only retained")
