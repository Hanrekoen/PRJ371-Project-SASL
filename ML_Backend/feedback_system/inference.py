import numpy as np
import json
import time
from tensorflow.keras.models import load_model

class SignClassifier:
    def __init__(self, model_path, labels):
        # Load Person 1's trained model[cite: 1]
        # Wrap in a try-except so your code doesn't crash if they haven't provided it yet
        try:
            self.model = load_model(model_path)
            self.model_loaded = True
        except IOError:
            print(f"Warning: Model not found at {model_path}. Running in dummy mode.")
            self.model_loaded = False
            
        self.labels = labels

    def predict(self, sequence):
        """
        Takes the array from Group 1, runs it through the model, 
        and times the execution to ensure it stays under 100ms[cite: 1].
        """
        start_time = time.time()
        
        if self.model_loaded:
            # sequence: numpy array shaped (SEQ_LEN, N_FEATURES)[cite: 1]
            batch = np.expand_dims(sequence, axis=0)
            probs = self.model.predict(batch, verbose=0)[0]
            idx = int(np.argmax(probs))
            predicted_sign = self.labels[idx]
            confidence = float(probs[idx])
            all_scores = {self.labels[i]: float(p) for i, p in enumerate(probs)}
        else:
            # Dummy logic so you can test your code without Person 1's model
            predicted_sign = "HELLO"
            confidence = 0.92
            all_scores = {"HELLO": 0.92, "THANK_YOU": 0.08}
            
        execution_time_ms = (time.time() - start_time) * 1000

        # Generate the feedback package for Unity
        unity_payload = self.generate_unity_payload(predicted_sign, confidence)
        
        return {
            'latency_ms': execution_time_ms,
            'raw_scores': all_scores,
            'unity_payload': unity_payload
        }

    def generate_unity_payload(self, sign, confidence):
        """
        Translates raw confidence into visual corrections based on project thresholds[cite: 1].
        Outputs a JSON string easily deserialized by Group 4's C# Unity environment.
        """
        if confidence > 0.85:
            status = "correct"
            message = "Pass"
        elif 0.60 <= confidence <= 0.85:
            status = "hint"
            message = "Close! Try adjusting your hand position."
        else:
            status = "incorrect"
            message = "Not recognised. Try again."

        payload = {
            "predicted_sign": sign,
            "confidence_score": round(confidence, 4),
            "status": status,
            "user_message": message
        }
        
        return json.dumps(payload)

# --- Test Execution ---
# Run this file directly to test the dummy logic
if __name__ == "__main__":
    # Dummy list of signs from Group 3
    test_labels = ["HELLO", "THANK_YOU", "PLEASE"]
    
    # Initialize the class pointing to a fake model path
    classifier = SignClassifier(model_path="models/sasl_lstm_v1.h5", labels=test_labels)
    
    # Create a dummy array shaped (30 frames, 126 features) to simulate Group 1's webcam data[cite: 1]
    dummy_sequence = np.random.rand(30, 126)
    
    # Run the prediction
    result = classifier.predict(dummy_sequence)
    
    print(f"Latency: {result['latency_ms']:.2f} ms")
    print(f"Payload for Unity: {result['unity_payload']}")