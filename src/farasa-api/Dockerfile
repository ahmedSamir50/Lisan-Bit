FROM python:3.11-slim

# Install Java Runtime Environment (required by Farasa jar files)
RUN apt-get update && \
    apt-get install -y --no-install-recommends default-jre-headless && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Install Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy and extract pre-downloaded Farasa JARs (tar.gz from farasa.qcri.org).
# Place FarasaSegmenterJar.jar.tar.gz and FarasaPOSJar.jar.tar.gz next to this Dockerfile.
COPY FarasaSegmenterJar.jar.tar.gz /tmp/
COPY FarasaPOSJar.jar.tar.gz       /tmp/

RUN mkdir -p /app/farasa_jars \
 && tar -xzf /tmp/FarasaSegmenterJar.jar.tar.gz -C /app/farasa_jars --strip-components=0 \
 && tar -xzf /tmp/FarasaPOSJar.jar.tar.gz       -C /app/farasa_jars --strip-components=0 \
 && rm /tmp/FarasaSegmenterJar.jar.tar.gz /tmp/FarasaPOSJar.jar.tar.gz \
 # Rename to the canonical names farasapy expects
 && find /app/farasa_jars -name "*Segmenter*.jar" | head -1 | xargs -I{} mv {} /app/farasa_jars/FarasaSegmenterJar.jar 2>/dev/null || true \
 && find /app/farasa_jars -name "*POS*.jar"       | head -1 | xargs -I{} mv {} /app/farasa_jars/FarasaPOSTagger.jar   2>/dev/null || true \
 && ls -lh /app/farasa_jars/

# Tell farasapy where the JARs live so it does not try to re-download them.
ENV FARASA_PATH=/app/farasa_jars

# Copy application source
COPY main.py .

EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
