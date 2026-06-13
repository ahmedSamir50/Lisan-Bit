# ==============================================================================
# Lisan Bits: Python Trainer Web API Service (FastAPI)
# ==============================================================================
# This web service wraps the PyTorch training script. It allows C# (.NET)
# to communicate with Python over standard HTTP.
# We use FastAPI because it is extremely fast, asynchronous, and easy to use.
# ==============================================================================

import os  # System-level utility module for managing paths and directories
import subprocess  # Module to execute external commands and capture their standard outputs
from fastapi import FastAPI, BackgroundTasks, HTTPException, Request  # Core FastAPI components
from fastapi.responses import FileResponse  # response class for streaming files (like model.pt)
from pydantic import BaseModel  # Library for defining data models and validating incoming JSON request payloads

# ------------------------------------------------------------------------------
# 1. Initialization and Global State
# ------------------------------------------------------------------------------
# We initialize our FastAPI application object.
app = FastAPI(title="Lisan Bits Python Trainer Service")

# A global dictionary acting as our in-memory database to keep track of the
# training job status, metrics, and logs so that C# can inspect them via HTTP.
STATUS = {
    "status": "Idle",         # Current state: Idle, Pending, Training, Completed, Failed
    "epochs": 0,              # Target epochs for training
    "current_epoch": 0,      # Currently active epoch number
    "loss": 0.0,              # Current loss score
    "logs": []                # String buffer storing terminal output logs line-by-line
}

# ------------------------------------------------------------------------------
# 2. Request Schemas
# ------------------------------------------------------------------------------
# Pydantic validates incoming JSON request payloads automatically.
# If C# sends incorrect properties or datatypes, FastAPI returns a 422 error automatically.
# ------------------------------------------------------------------------------
class TrainRequest(BaseModel):
    vocab_size: int = 1000  # Cap on unique vocabulary size (defaults to 1000)
    dim: int = 128          # Dimensions for word embeddings (defaults to 128)
    epochs: int = 5         # Number of epochs (iterations) to train (defaults to 5)
    lr: float = 0.025       # Learning rate for gradient descent optimizer (defaults to 0.025)

# ------------------------------------------------------------------------------
# 3. Background Job Runner
# ------------------------------------------------------------------------------
# Neural network training is a slow, CPU/GPU heavy process.
# We cannot run it in the main web thread because it would freeze the web API.
# Instead, we run it inside a background task, capturing console outputs.
# ------------------------------------------------------------------------------
def run_training_background(req: TrainRequest):
    """
    Asynchronously executes the train_model.py script and monitors logs.
    """
    global STATUS
    
    # Update state: training is starting
    STATUS["status"] = "Training"
    STATUS["logs"] = []
    STATUS["current_epoch"] = 0
    STATUS["loss"] = 0.0
    STATUS["epochs"] = req.epochs
    
    corpus_path = "training_corpus.txt"
    # If no corpus was uploaded by C#, create a dummy file to ensure training builds.
    if not os.path.exists(corpus_path):
        with open(corpus_path, "w", encoding="utf-8") as f:
            f.write("العربية لغة جميلة\nتعلم اللغة العربية مفيد جدا\nالذكاء الاصطناعي في خدمة اللغة\n")

    output_path = "model.pt"  # Target path for exported model
    
    # Formulate command line command to run the Python training script
    cmd = [
        "python", "train_model.py",
        "--input", corpus_path,
        "--output", output_path,
        "--vocab_size", str(req.vocab_size),
        "--dim", str(req.dim),
        "--epochs", str(req.epochs),
        "--lr", str(req.lr)
    ]
    
    try:
        # Popen spawns the command as a new child process.
        # stdout=subprocess.PIPE redirects standard console outputs so Python can read them.
        # stderr=subprocess.STDOUT merges errors with standard output.
        # text=True decodes binary bytes into readable strings.
        # bufsize=1 enables line-by-line stdout buffering.
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
        
        # Read the stdout streams in real-time as lines are printed by train_model.py
        for line in proc.stdout:
            line_str = line.strip()
            if line_str:
                # Add terminal log line to our global status log list
                STATUS["logs"].append(line_str)
                
                # Check if the line reports epoch loss (e.g. "Epoch 1/5 completed. Average Loss: 0.1234")
                if "Epoch" in line_str and "Loss:" in line_str:
                    try:
                        # Extract epoch count and loss score by string splits
                        parts = line_str.split()
                        epoch_part = parts[1].split('/')[0]
                        STATUS["current_epoch"] = int(epoch_part)
                        
                        loss_part = line_str.split("Loss:")[1].strip()
                        STATUS["loss"] = float(loss_part)
                    except Exception:
                        pass  # Fail-safe: if parsing fails, ignore and continue printing logs
                        
        # Block and wait for the process to complete execution
        proc.wait()
        
        # Check if python execution succeeded (exit code 0 means success)
        if proc.returncode == 0:
            STATUS["status"] = "Completed"
        else:
            STATUS["status"] = "Failed"
            STATUS["logs"].append(f"Exit code: {proc.returncode}")
    except Exception as e:
        STATUS["status"] = "Failed"
        STATUS["logs"].append(str(e))

# ------------------------------------------------------------------------------
# 4. HTTP Endpoints
# ------------------------------------------------------------------------------

# Route A: POST /train - Triggers PyTorch training in a background thread
@app.post("/train")
def start_training(req: TrainRequest, background_tasks: BackgroundTasks):
    """
    Triggers the training run.
    """
    global STATUS
    # Prevent starting a new training run if one is already active
    if STATUS["status"] == "Training":
        raise HTTPException(status_code=400, detail="Training already in progress")
        
    STATUS["status"] = "Pending"
    # FastAPI registers 'run_training_background' to execute immediately after we return HTTP response.
    # This prevents blocking the C# client call!
    background_tasks.add_task(run_training_background, req)
    return {"message": "Training started"}

# Route B: POST /upload-corpus - Receives raw text from C# and saves it locally
@app.post("/upload-corpus")
async def upload_corpus(request: Request):
    """
    Accepts raw text payload from the C# client containing sentences to train on.
    """
    # Read the raw HTTP request body bytes
    body = await request.body()
    # Decode raw bytes into standard UTF-8 string
    text = body.decode("utf-8")
    
    # Save the text corpus onto local workspace disk for train_model.py to consume
    with open("training_corpus.txt", "w", encoding="utf-8") as f:
        f.write(text)
        
    return {"message": "Corpus uploaded successfully"}

# Route C: GET /status - Returns active training progress metrics and console logs
@app.get("/status")
def get_status():
    """
    Exposes global STATUS dictionary. Used by C# to poll progress.
    """
    return STATUS

# Route D: GET /model - Serves model.pt file as a direct download stream
@app.get("/model")
def get_model():
    """
    Allows C# to download the compiled model.pt file upon completion.
    """
    model_path = "model.pt"
    # Return 404 error if model hasn't been generated yet
    if not os.path.exists(model_path):
        raise HTTPException(status_code=404, detail="Model file not found. Perform training first.")
    
    # FileResponse handles streaming binary data from disk to client with correct download headers
    return FileResponse(model_path, filename="model.pt")
