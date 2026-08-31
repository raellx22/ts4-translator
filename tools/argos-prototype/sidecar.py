from __future__ import annotations

import argparse
import http.server
import json
import os
import re
import sys
import threading
import time
import traceback
import types
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

TOKEN_PATTERN = re.compile(r"__TS4_TOKEN_\d{4}__")


def emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def protect_tokens(text: str) -> tuple[str, list[str]]:
    tokens: list[str] = []

    def replace(match: re.Match[str]) -> str:
        tokens.append(match.group(0))
        index = len(tokens) - 1
        if index >= 0x1900:
            raise ValueError("Too many protected tokens in one string.")
        return chr(0xE000 + index)

    return TOKEN_PATTERN.sub(replace, text), tokens


def restore_tokens(text: str, tokens: list[str], source: str) -> str:
    for index, token in enumerate(tokens):
        text = text.replace(chr(0xE000 + index), token)
        if re.search(re.escape(token) + r"\s", source) and not re.search(re.escape(token) + r"\s", text):
            text = text.replace(token, token + " ", 1)
        if re.search(r"\s" + re.escape(token), source) and not re.search(r"\s" + re.escape(token), text):
            text = text.replace(token, " " + token, 1)
    return text


def main() -> None:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--server", action="store_true")
    parser.add_argument("--port-file")
    parser.add_argument("--token")
    parser.add_argument("--packages-dir")
    parser.add_argument("--runtime-root")
    args = parser.parse_args()

    if args.packages_dir:
        os.environ["ARGOS_PACKAGES_DIR"] = args.packages_dir
    if args.runtime_root:
        os.environ["XDG_DATA_HOME"] = os.path.join(args.runtime_root, "data")
        os.environ["XDG_CONFIG_HOME"] = os.path.join(args.runtime_root, "config")
        os.environ["XDG_CACHE_HOME"] = os.path.join(args.runtime_root, "cache")
    os.environ["ARGOS_DEVICE_TYPE"] = "cpu"
    os.environ["ARGOS_CHUNK_TYPE"] = "MINISBD"
    os.environ["ARGOS_BEAM_SIZE"] = "1"
    os.environ["ARGOS_BATCH_SIZE"] = "64"
    os.environ["ARGOS_INTER_THREADS"] = "1"
    os.environ["ARGOS_INTRA_THREADS"] = "0"

    # Argos 1.11 imports Stanza unconditionally even when MiniSBD is explicitly selected. The app's
    # managed runtime deliberately omits Stanza/PyTorch; provide the unused symbol so importing the
    # lightweight MiniSBD path does not force a 100+ MB PyTorch dependency.
    if "stanza" not in sys.modules:
        stanza_stub = types.ModuleType("stanza")
        stanza_stub.Pipeline = lambda *args, **kwargs: (_ for _ in ()).throw(
            RuntimeError("Stanza is not available in the TS4Translator Argos runtime.")
        )
        sys.modules["stanza"] = stanza_stub

    import argostranslate.package
    import argostranslate.settings
    import argostranslate.translate
    import ctranslate2

    translation_cache: dict[tuple[str, str], object] = {}

    def get_translation(source: str, target: str):
        key = (source, target)
        if key not in translation_cache:
            languages = argostranslate.translate.get_installed_languages()
            source_language = next((language for language in languages if language.code == source), None)
            target_language = next((language for language in languages if language.code == target), None)
            if source_language is None or target_language is None:
                raise RuntimeError(f"Argos model {source}->{target} is not installed.")
            translation_cache[key] = source_language.get_translation(target_language)
        return translation_cache[key]

    def translate_one(translation, text: str) -> str:
        protected, tokens = protect_tokens(text)
        translated = translation.translate(protected)
        return restore_tokens(translated, tokens, text)

    def translate_many_base(translation, texts: list[str]) -> list[str]:
        """Batch all sentences from all STBL entries in one CTranslate2 call.

        Argos' public batch boundary is one string at a time. The pinned 1.11 implementation already
        calls CTranslate2.translate_batch internally, but doing that separately for thousands of tiny
        UI strings leaves most batches nearly empty. This adapter preserves Argos tokenization,
        sentence splitting and decoding while filling a single cross-entry CTranslate2 batch.
        """
        underlying = getattr(translation, "underlying", translation)
        if not hasattr(underlying, "pkg") or not hasattr(underlying, "sentencizer"):
            return [translate_one(translation, text) for text in texts]

        if underlying.translator is None:
            underlying.translator = ctranslate2.Translator(
                str(underlying.pkg.package_path / "model"),
                device=argostranslate.settings.device,
                inter_threads=argostranslate.settings.inter_threads,
                intra_threads=argostranslate.settings.intra_threads,
                compute_type=argostranslate.settings.compute_type,
            )

        all_tokenized: list[list[str]] = []
        item_ranges: list[tuple[list[tuple[int, int] | None], list[str]]] = []
        for text in texts:
            protected, tokens = protect_tokens(text)
            paragraph_ranges: list[tuple[int, int] | None] = []
            for paragraph in protected.split("\n"):
                if paragraph == "":
                    paragraph_ranges.append(None)
                    continue
                sentences = underlying.sentencizer.split_sentences(paragraph) or [paragraph]
                start = len(all_tokenized)
                all_tokenized.extend(underlying.pkg.tokenizer.encode(sentence) for sentence in sentences)
                paragraph_ranges.append((start, len(all_tokenized)))
            item_ranges.append((paragraph_ranges, tokens))

        target_prefix = None
        if underlying.pkg.target_prefix != "":
            target_prefix = [[underlying.pkg.target_prefix]] * len(all_tokenized)

        translated_batches = underlying.translator.translate_batch(
            all_tokenized,
            target_prefix=target_prefix,
            replace_unknowns=True,
            max_batch_size=argostranslate.settings.batch_size,
            batch_type="tokens",
            beam_size=argostranslate.settings.beam_size,
            num_hypotheses=1,
            length_penalty=0.2,
            return_scores=False,
        ) if all_tokenized else []

        results: list[str] = []
        for paragraph_ranges, tokens in item_ranges:
            translated_paragraphs: list[str] = []
            for paragraph_range in paragraph_ranges:
                if paragraph_range is None:
                    translated_paragraphs.append("")
                    continue
                start, end = paragraph_range
                translated_tokens: list[str] = []
                for translated_batch in translated_batches[start:end]:
                    translated_tokens.extend(translated_batch.hypotheses[0])
                value = underlying.pkg.tokenizer.decode(translated_tokens)
                if underlying.pkg.target_prefix != "" and value.startswith(underlying.pkg.target_prefix):
                    value = value[len(underlying.pkg.target_prefix):]
                translated_paragraphs.append(value[1:] if value.startswith(" ") else value)
            results.append(restore_tokens("\n".join(translated_paragraphs), tokens, texts[len(results)]))
        return results

    def translate_many(translation, texts: list[str]) -> list[str]:
        results = translate_many_base(translation, texts)
        failed_indexes = [
            index for index, (source, result) in enumerate(zip(texts, results, strict=True))
            if Counter(TOKEN_PATTERN.findall(source)) != Counter(TOKEN_PATTERN.findall(result))
        ]
        if not failed_indexes:
            return results

        # Guaranteed-preservation fallback: translate every natural-language segment between opaque
        # TS4 tokens as part of one shared CTranslate2 batch, then interleave the untouched tokens.
        segment_owners: list[tuple[int, int]] = []
        split_items: dict[int, list[str]] = {}
        segments_to_translate: list[str] = []
        for item_index in failed_indexes:
            parts = TOKEN_PATTERN.split(texts[item_index])
            tokens = TOKEN_PATTERN.findall(texts[item_index])
            split_items[item_index] = tokens
            for part_index, part in enumerate(parts):
                if part:
                    segment_owners.append((item_index, part_index))
                    segments_to_translate.append(part)

        translated_segments = translate_many_base(translation, segments_to_translate)
        translated_by_item: dict[int, dict[int, str]] = {}
        for owner, translated in zip(segment_owners, translated_segments, strict=True):
            translated_by_item.setdefault(owner[0], {})[owner[1]] = translated

        for item_index in failed_indexes:
            parts = TOKEN_PATTERN.split(texts[item_index])
            tokens = split_items[item_index]
            rebuilt: list[str] = []
            for part_index, part in enumerate(parts):
                rebuilt.append(translated_by_item.get(item_index, {}).get(part_index, part))
                if part_index < len(tokens):
                    rebuilt.append(tokens[part_index])
            results[item_index] = "".join(rebuilt)
        return results

    def handle_request(request: dict) -> dict:
        request_id = None
        try:
            request_id = request.get("id")
            command = request.get("command")
            if command == "ping":
                return {"id": request_id, "ok": True, "version": "1"}
            elif command == "status":
                packages = argostranslate.package.get_installed_packages()
                return {
                    "id": request_id,
                    "ok": True,
                    "packages": [
                        {"from": package.from_code, "to": package.to_code, "version": package.package_version}
                        for package in packages
                    ],
                }
            elif command == "install_model":
                path = request["path"]
                argostranslate.package.install_from_path(path)
                translation_cache.clear()
                argostranslate.translate.get_installed_languages.cache_clear()
                return {"id": request_id, "ok": True}
            elif command == "translate":
                source = request.get("source", "en")
                target = request.get("target", "pb")
                items = request.get("items", [])
                workers = max(1, min(int(request.get("workers", 1)), 8))
                translation = get_translation(source, target)
                started = time.perf_counter()

                if workers == 1:
                    translations = translate_many(translation, [item["text"] for item in items])
                else:
                    with ThreadPoolExecutor(max_workers=workers) as executor:
                        translations = list(executor.map(lambda item: translate_one(translation, item["text"]), items))

                return {
                    "id": request_id,
                    "ok": True,
                    "elapsedMs": round((time.perf_counter() - started) * 1000, 3),
                    "items": [
                        {"id": item["id"], "text": translated}
                        for item, translated in zip(items, translations, strict=True)
                    ],
                }
            elif command == "shutdown":
                return {"id": request_id, "ok": True}
            else:
                raise ValueError(f"Unknown command: {command}")
        except Exception as error:
            return {
                "id": request_id,
                "ok": False,
                "error": str(error),
                "type": type(error).__name__,
                "trace": traceback.format_exc(),
            }

    if args.server:
        if not args.port_file or not args.token:
            raise ValueError("--server requires --port-file and --token")

        class RequestHandler(http.server.BaseHTTPRequestHandler):
            def do_POST(self) -> None:
                if self.path != "/command" or self.headers.get("X-TS4-Argos-Token") != args.token:
                    self.send_error(403)
                    return
                try:
                    content_length = int(self.headers.get("Content-Length", "0"))
                    if content_length <= 0 or content_length > 64 * 1024 * 1024:
                        raise ValueError("Invalid request size.")
                    request = json.loads(self.rfile.read(content_length))
                    response = handle_request(request)
                    payload = json.dumps(response, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
                    self.send_response(200)
                    self.send_header("Content-Type", "application/json; charset=utf-8")
                    self.send_header("Content-Length", str(len(payload)))
                    self.end_headers()
                    self.wfile.write(payload)
                    self.wfile.flush()
                    if request.get("command") == "shutdown":
                        threading.Thread(target=self.server.shutdown, daemon=True).start()
                except Exception as error:
                    payload = json.dumps({
                        "id": None,
                        "ok": False,
                        "error": str(error),
                        "type": type(error).__name__,
                    }, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
                    self.send_response(400)
                    self.send_header("Content-Type", "application/json; charset=utf-8")
                    self.send_header("Content-Length", str(len(payload)))
                    self.end_headers()
                    self.wfile.write(payload)

            def log_message(self, format: str, *values: object) -> None:
                pass

        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), RequestHandler)
        port_payload = json.dumps({"port": server.server_port, "pid": os.getpid()}, separators=(",", ":"))
        partial_path = args.port_file + ".partial"
        with open(partial_path, "w", encoding="utf-8", newline="\n") as port_stream:
            port_stream.write(port_payload)
            port_stream.flush()
            os.fsync(port_stream.fileno())
        os.replace(partial_path, args.port_file)
        server.serve_forever()
        server.server_close()
        return

    for raw_line in sys.stdin:
        request = json.loads(raw_line)
        response = handle_request(request)
        emit(response)
        if request.get("command") == "shutdown":
            return


if __name__ == "__main__":
    main()
