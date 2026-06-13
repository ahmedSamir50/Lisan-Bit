# ==============================================================================
# Lisan Bits: Standalone Python Training Script
# ==============================================================================
# This script is responsible for building a vocabulary, parsing the dataset,
# defining the neural network model, and training it using PyTorch.
# Finally, it exports the model in TorchScript format so C# can load it natively.
# ==============================================================================

import os  # System-level utility module for managing paths and directories
import sys  # System-specific parameters and functions (like exiting the script)
import argparse  # Module for parsing command-line flags and parameters (e.g., --epochs)
import torch  # The main PyTorch library containing tensor data types and math operations
import torch.nn as nn  # "Neural Network" module containing layers (e.g., Embedding, Linear)
import torch.optim as optim  # "Optimizer" module containing gradient descent algorithms (e.g., Adam)

# ------------------------------------------------------------------------------
# 1. Model Definition (Neural Network Architecture)
# ------------------------------------------------------------------------------
# In PyTorch, all neural networks inherit from the base class `nn.Module`.
# This class acts as a container for all network layers and weight parameters.
# ------------------------------------------------------------------------------
class LisanModel(nn.Module):
    def __init__(self, vocab_size, embedding_dim):
        """
        The constructor is where we define the trainable layers of our network.
        
        vocab_size: How many unique words are in our dictionary (e.g. 1000).
        embedding_dim: How many numbers represent a single word's meaning (e.g. 128).
        """
        super().__init__()  # Calls the parent constructor (nn.Module) to register PyTorch internal hooks
        
        # 1. Embedding Layer: Maps a word ID (an integer) to a dense vector (list of floats).
        # Internally, it is a matrix of size (vocab_size, embedding_dim) that will be trained.
        self.embeddings = nn.Embedding(vocab_size, embedding_dim)
        
        # 2. Linear Layer: Project the word meaning back into the vocabulary space.
        # It maps the embedding vector (size 128) to logits (scores) for each word in the vocab (size 1000).
        # We use this to predict which word comes next in a sentence.
        self.linear = nn.Linear(embedding_dim, vocab_size)

    def forward(self, input_ids):
        """
        The forward pass defines how data flows through the neural network.
        This is called when we execute the model (e.g. output = model(inputs)).
        
        input_ids: A list (tensor) of word IDs. Shape: (batch_size)
        """
        # Step A: Lookup the word meanings (embeddings) for the input word IDs.
        # Output shape: (batch_size, embedding_dim)
        embeds = self.embeddings(input_ids)
        
        # Step B: Project the meanings to vocabulary logits (scores).
        # Output shape: (batch_size, vocab_size)
        logits = self.linear(embeds)
        
        # Return the logits (unnormalized log probabilities for each word in the vocabulary)
        return logits

# ------------------------------------------------------------------------------
# 2. In-Container Training Loop
# ------------------------------------------------------------------------------
# This function executes the complete workflow:
# Loading corpus -> Building Vocab -> Creating Dataset -> Training -> Scripting
# ------------------------------------------------------------------------------
def train(args):
    print("Starting python model training script inside container...")
    print(f"Arguments: {args}")

    # Ensure the directory path where we intend to save the model exists.
    # If the directory doesn't exist, create it recursively.
    os.makedirs(os.path.dirname(args.output), exist_ok=True)

    # 1. Read input dataset
    # Verify that the corpus text file exists. If not, exit with code 1.
    if not os.path.exists(args.input):
        print(f"Error: Input dataset file not found at {args.input}")
        sys.exit(1)

    print(f"Reading corpus from {args.input}...")
    # Open the text file using UTF-8 encoding (to support Arabic characters properly)
    with open(args.input, "r", encoding="utf-8") as f:
        # Read lines, strip whitespace (spaces, newlines), and ignore empty lines
        lines = [line.strip() for line in f if line.strip()]

    print(f"Loaded {len(lines)} sentences/lines.")

    # 2. Basic tokenization & Vocab building
    word_counts = {}  # Dictionary to keep track of how many times each word appears
    tokenized_sentences = []  # List to store lists of words for each sentence
    
    for line in lines:
        tokens = line.split()  # Split sentence on spaces/whitespace into list of words
        if tokens:
            tokenized_sentences.append(tokens)  # Save the tokenized sentence
            for token in tokens:
                # Increment count for this token (word)
                word_counts[token] = word_counts.get(token, 0) + 1

    # Initialize vocabulary with an Unknown Token (<UNK>) to handle out-of-vocabulary words.
    vocab = ["<UNK>"]
    # Sort words by frequency (highest count first) and add to vocabulary
    for word, count in sorted(word_counts.items(), key=lambda x: x[1], reverse=True):
        if count >= 2:  # Only include words that appear at least twice to avoid noise
            vocab.append(word)

    # Calculate vocabulary size, capped at the user-specified maximum vocabulary size
    vocab_size = min(len(vocab), args.vocab_size)
    
    # Create a mapping dictionary: Word (string) -> Unique ID (integer)
    word_to_id = {word: idx for idx, word in enumerate(vocab[:vocab_size])}
    print(f"Vocabulary Size: {vocab_size}")

    # 3. Create training pairs (Simple Language Modeling task)
    # The task: given word 'A', predict next word 'B'.
    inputs = []   # Input word IDs (e.g. index of "تعلم")
    targets = []  # Target word IDs (e.g. index of "اللغة")
    
    for tokens in tokenized_sentences:
        # Convert words in the sentence to their corresponding ID (default to <UNK> index 0)
        ids = [word_to_id.get(token, 0) for token in tokens]
        # Iterate through sentence and create pairs: (word_t, word_t+1)
        for i in range(len(ids) - 1):
            inputs.append(ids[i])
            targets.append(ids[i+1])

    # If the text file was empty or too small to generate pairs, create dummy validation pairs.
    if not inputs:
        print("Dataset too small, fallback to dummy inputs for validation.")
        inputs = [0, 1, 0, 1]  # Dummy input IDs
        targets = [1, 0, 1, 0]  # Dummy target IDs

    # Convert native Python lists of integers into PyTorch Tensors.
    # Tensors are multi-dimensional arrays optimized for fast math (especially on GPUs/CPUs).
    # 'dtype=torch.long' specifies 64-bit integer values (required for embedding indices).
    inputs_t = torch.tensor(inputs, dtype=torch.long)
    targets_t = torch.tensor(targets, dtype=torch.long)

    # 4. Initialize model components
    model = LisanModel(vocab_size=vocab_size, embedding_dim=args.dim)
    
    # Cross Entropy Loss: measures how good the model predictions are.
    # It compares predicted logits with correct target word IDs. Lower loss = better predictions.
    criterion = nn.CrossEntropyLoss()
    
    # Adam Optimizer: handles weight updates based on calculation of gradients (rates of change).
    # 'lr' is the learning rate (step size during gradient descent).
    optimizer = optim.Adam(model.parameters(), lr=args.lr)

    # 5. Training loop
    dataset_size = len(inputs)
    # Train in mini-batches rather than loading the whole dataset at once to save memory.
    batch_size = min(128, dataset_size)
    
    print("Training started...")
    for epoch in range(1, args.epochs + 1):
        epoch_loss = 0.0  # Sum of loss values across this epoch
        steps = 0  # Count of batches processed
        
        # Shuffle the indices randomly at the start of each epoch to prevent ordering bias
        permutation = torch.randperm(dataset_size)

        # Process the dataset in increments of 'batch_size'
        for i in range(0, dataset_size, batch_size):
            # Extract a slice of randomized indices for this batch
            indices = permutation[i:i + batch_size]
            
            # Fetch the actual input and target tensors for this batch
            batch_x = inputs_t[indices]
            batch_y = targets_t[indices]

            # A. Reset gradients to zero before backpropagation.
            # PyTorch accumulates gradients by default; resetting prevents mixing with previous batch.
            optimizer.zero_grad()
            
            # B. Forward Pass: Run input data through the model to get predicted logits.
            logits = model(batch_x)
            
            # C. Compute Loss: Measure difference between predictions and correct target targets.
            loss = criterion(logits, batch_y)
            
            # D. Backward Pass (Backpropagation): Compute gradients of the loss with respect
            # to all model weights. This determines how to tweak weights to improve predictions.
            loss.backward()
            
            # E. Weight Update Step: Modify model weights slightly in the direction
            # that reduces loss, using the computed gradients and the Adam algorithm.
            optimizer.step()

            # Accumulate the batch loss (weighted by the number of items in the batch)
            epoch_loss += loss.item() * len(indices)
            steps += 1

        # Calculate and display the average loss for this epoch
        avg_loss = epoch_loss / dataset_size
        print(f"Epoch {epoch}/{args.epochs} completed. Average Loss: {avg_loss:.4f}")

    # 6. Save model as TorchScript (.pt)
    # TorchScript is a way to compile a PyTorch model into an intermediate representation.
    # This compiled model can be run in environments without Python, like C# / .NET!
    print("Scripting model to TorchScript format...")
    
    # Set model to evaluation mode (turns off dropout or batch normalization if used).
    model.eval()
    
    # Dummy input required for tracing (though scripting doesn't strictly need it, we keep it as fallback)
    dummy_input = torch.tensor([0], dtype=torch.long)
    
    try:
        # Option A: Scripting.
        # This compiles the Python class source code into C++ friendly TorchScript.
        # Works perfectly for standard neural networks.
        scripted_model = torch.jit.script(model)
        scripted_model.save(args.output)  # Save compilation result to the requested path
        print(f"Trained model exported successfully to: {args.output}")
    except Exception as e:
        print(f"Failed to script model: {e}")
        try:
            # Option B: Tracing (Fallback).
            # Tracing runs a dummy input through the network and records the operations performed.
            traced_model = torch.jit.trace(model, dummy_input)
            traced_model.save(args.output)  # Save traced result
            print(f"Trained model exported successfully via tracing to: {args.output}")
        except Exception as trace_err:
            print(f"Failed to trace model: {trace_err}")
            sys.exit(1)

# ------------------------------------------------------------------------------
# 3. CLI Entrypoint
# ------------------------------------------------------------------------------
# This checks if the script is run directly from the shell or command line.
# If yes, it parses the command flags and starts the training.
# ------------------------------------------------------------------------------
if __name__ == "__main__":
    # Create command line arguments parser
    parser = argparse.ArgumentParser(description="Lisan Bits Model Trainer")
    
    # Flag '--input': path to raw text file containing sentences (string, required)
    parser.add_argument("--input", type=str, required=True, help="Path to input text dataset")
    
    # Flag '--output': path where the compiled model (.pt) should be saved (string, required)
    parser.add_argument("--output", type=str, required=True, help="Path to save output .pt model")
    
    # Flag '--vocab_size': maximum number of words to index (integer, default 1000)
    parser.add_argument("--vocab_size", type=int, default=1000, help="Vocabulary size")
    
    # Flag '--dim': word embedding dimensions (integer, default 128)
    parser.add_argument("--dim", type=int, default=128, help="Embedding dimension")
    
    # Flag '--epochs': how many times to scan the dataset during training (integer, default 5)
    parser.add_argument("--epochs", type=int, default=5, help="Number of training epochs")
    
    # Flag '--lr': gradient step learning rate (float, default 0.025)
    parser.add_argument("--lr", type=float, default=0.025, help="Learning rate")
    
    # Parse the command line flags into a structured Python object
    args = parser.parse_args()
    
    # Call the training routine with parsed arguments
    train(args)
