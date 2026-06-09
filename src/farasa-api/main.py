import logging
from fastapi import FastAPI
from pydantic import BaseModel
from typing import List

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("farasa-api")

app = FastAPI(title="Farasa NLP API")

# ---------------------------------------------------------------------------
# Attempt to load real Farasa (requires Java + farasapy).
# Falls back to a naive tokeniser if Farasa jars are unavailable.
# ---------------------------------------------------------------------------
try:
    import os
    from farasa.segmenter import FarasaSegmenter
    from farasa.pos import FarasaPOSTagger

    # Pass the local JAR paths directly to prevent automatic download checks
    _segmenter  = FarasaSegmenter(binary_path="/app/farasa_jars/FarasaSegmenterJar.jar", interactive=False)
    _pos_tagger = FarasaPOSTagger(binary_path="/app/farasa_jars/FarasaPOSTagger.jar", interactive=False)
    USE_FARASA = True
    logger.info("Farasa initialized with local JAR files successfully.")
except Exception as exc:
    logger.warning("Farasa initialization failed (%s). Using fallback tokeniser.", exc)
    USE_FARASA = False
    _segmenter = None
    _pos_tagger = None


class TextRequest(BaseModel):
    text: str

class TokenResponse(BaseModel):
    word: str
    root: str
    pos: str

class AnalysisResponse(BaseModel):
    tokens: List[TokenResponse]


def _fallback_analyze(text: str) -> List[TokenResponse]:
    """Naive fallback when Farasa is unavailable."""
    results = []
    for word in text.split():
        clean = word.lstrip("\u0627\u0644")          # strip definite article \u0627\u0644
        root = clean[:3] if len(clean) >= 3 else clean
        pos = "NOUN" if word.startswith("\u0627\u0644") else "VERB"
        results.append(TokenResponse(word=word, root=root, pos=pos))
    return results


@app.post("/analyze", response_model=AnalysisResponse)
def analyze_text(request: TextRequest):
    if USE_FARASA and _segmenter and _pos_tagger:
        try:
            segmented: str = _segmenter.segment(request.text)
            tagged: str    = _pos_tagger.tag(request.text)
            # tagged is a string like "word/POS word/POS ..."
            tokens = []
            for token in tagged.split():
                parts = token.rsplit("/", 1)
                word = parts[0]
                pos  = parts[1] if len(parts) == 2 else "NOUN"
                # Use segmenter output to approximate root (first segment)
                seg_parts = word.split("+")
                root = seg_parts[0][:3] if seg_parts[0] else word[:3]
                tokens.append(TokenResponse(word=word, root=root, pos=pos))
            return AnalysisResponse(tokens=tokens)
        except Exception as exc:
            logger.warning("Farasa analysis failed (%s), using fallback.", exc)

    return AnalysisResponse(tokens=_fallback_analyze(request.text))


@app.get("/health")
def health_check():
    return {"status": "healthy", "farasa_active": USE_FARASA}
