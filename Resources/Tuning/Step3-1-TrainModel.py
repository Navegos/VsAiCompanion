# This script is dedicated to training the model with a GPU if available. It imports the appropriate classes and methods from PyTorch, transformers, and datasets libraries. The script defines a helper function get_device to determine if a GPU is available for training, and it sets up the training device accordingly. It then sets the paths to the model and tokenized data, loads the tokenizer and model, configures the model with the pad token, and loads the tokenized dataset. Training arguments are defined for fine-tuning the model, tailored specifically for language generation tasks.

# The train_model function initializes the Trainer class with the model, training arguments, and training dataset, and it begins training. After training, the script saves the fine-tuned model and tokenizer to the pre-defined output directory. This script is designed as a main program that performs the training and can be executed directly to fine-tune the model.

import os
import torch
import json
import logging
from transformers import AutoModelForCausalLM, AutoTokenizer, Trainer, TrainingArguments
from datasets import load_from_disk
import gc

# Setup logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Log the starting of the training process
logger.info("Starting the training process...")

# Load configuration from a JSON file
with open('Step0-1-Config.json', 'r') as config_file:
    config = json.load(config_file)

# Path to the .pem file that contains the trusted root certificates
CERT_FILE_PATH = config.get('CERT_FILE_PATH')
# Specify the model name (as from Hugging Face Model Hub)
MODEL_NAME = config.get('MODEL_NAME')
# Specify the path to tokenized data
TOKENIZED_DATA_OUTPUT_DIR = config.get('TOKENIZED_DATA_OUTPUT_DIR')
# Define where you would like to cache models and tokenizers.
CACHE_DIR = config.get('CACHE_DIR')
# Define where you would like to save the fine-tuned model and tokenizer.
NEW_OUTPUT_DIR = config.get('NEW_OUTPUT_DIR')

# Only set the REQUESTS_CA_BUNDLE environment variable if the certificate file exists and is not empty
if os.path.exists(CERT_FILE_PATH) and os.path.getsize(CERT_FILE_PATH) > 0:
    os.environ['REQUESTS_CA_BUNDLE'] = os.path.abspath(CERT_FILE_PATH)

def get_device():
    # Should print the number of available CUDA devices
    print(f"CUDA Devices Count: {torch.cuda.device_count()}")
    # Should print the name of the CUDA device, likely your NVIDIA GPU
    print(f"CUDA Device Name: {torch.cuda.get_device_name(0)}") 
    print(f"Tensor Cores: {supports_tensor_cores()}") 
    # Check for available GPU
    if torch.cuda.is_available() & config["USE_GPU_CUDA"]:
        logger.info("Use GPU")
        device = torch.device("cuda:0")  # Use the second GPU (indexing starts at 0)
        # Create a tensor on GPU
        x = torch.randn(3, 3).cuda()
        # Perform some operations on the tensor
        y = x * 2 + 1
        # Move the tensor back to CPU
        z = y.cpu()
        print(z)
    else:
        # Log the starting of the training process
        logger.info("Use CPU")
        device = torch.device("cpu")
    return device
    
# Function to check if the GPU supports Tensor Cores
def supports_tensor_cores():
    if torch.cuda.is_available():
        compute_capability = torch.cuda.get_device_capability(torch.cuda.current_device())
        # Tensor cores are supported on devices with compute capability of 7.0 and higher
        return compute_capability[0] >= 7
    return False

# Function to get training arguments with fp16 set if Tensor Cores are supported
def get_training_arguments():
    # Check if the device has Tensor Cores which support FP16
    use_fp16 = supports_tensor_cores() & config["USE_GPU_TENSOR"]
    # Define training arguments
    training_args = TrainingArguments(
        output_dir=NEW_OUTPUT_DIR,
        overwrite_output_dir=True,
        do_train=True,
        # Further reduce batch size if necessary
        per_device_train_batch_size=1,
        per_device_eval_batch_size=1,
        # Adjust based on GPU memory after running tests
        gradient_accumulation_steps=4,
        # If your sequences are too long, decreasing this can help
        # max_seq_length=128 or another lower value
        num_train_epochs=3,
        logging_dir='./Logs',
        logging_steps=100,
        save_strategy="steps",
        save_steps=500,
        evaluation_strategy="steps",
        warmup_steps=100,
        weight_decay=0.01,
        # You may reduce the number of dataloading workers if memory is an issue
        # dataloader_num_workers=1 or 0
        fp16=use_fp16,
        # Additional parameters may be set as necessary
        # You can uncomment this if you want the script to clear the CUDA cache
        # disable_tqdm=False,
    )
    # Uncommend and add to the script if you want to force clear CUDA cache
    # torch.cuda.empty_cache()
    return training_args

device = get_device()

# Load the tokenizer and model specific to the Orca-2-7b
tokenizer = AutoTokenizer.from_pretrained(MODEL_NAME, cache_dir=CACHE_DIR)
model = AutoModelForCausalLM.from_pretrained(MODEL_NAME, cache_dir=CACHE_DIR)

# Ensure the model and tokenizer are correctly loaded
if not model or not tokenizer:
    raise ValueError("The model or tokenizer could not be loaded. Please check the MODEL_NAME and paths.")

model.to(device)

# Load the tokenized datasets from disk
tokenized_datasets = load_from_disk(TOKENIZED_DATA_OUTPUT_DIR)

# Check if 'train' split is available in tokenized datasets
if 'train' not in tokenized_datasets:
    raise ValueError("The 'train' split does not exist in the tokenized datasets. Please ensure that the dataset has been properly split and tokenized.")

# Use the get_training_arguments function to configure training
training_args = get_training_arguments()

# Initialize and train the model
def train_model(training_args, model, tokenized_datasets):
    trainer = Trainer(
        model=model,
        args=training_args,
        train_dataset=tokenized_datasets['train'],  # Provide the training dataset
    )
    trainer.train()

if __name__ == '__main__':
    # Train the model using the tokenized data
    train_model(training_args, model, tokenized_datasets)

    # Save the final model and tokenizer
    model.save_pretrained(training_args.output_dir)
    tokenizer.save_pretrained(training_args.output_dir)

# Log the completion of the training process
logger.info("Training process completed. Saving the model and tokenizer...")
