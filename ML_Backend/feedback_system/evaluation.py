import numpy as np
import pandas as pd
import seaborn as sns
import matplotlib.pyplot as plt
from sklearn.metrics import confusion_matrix, classification_report

class ModelEvaluator:
    def __init__(self, labels):
        self.labels = labels

    def generate_classification_report(self, y_true, y_pred):
        """
        Calculates precision, recall, and f1-score per sign.
        """
        print("\n--- Classification Report ---")
        report = classification_report(y_true, y_pred, target_names=self.labels, zero_division=0)
        print(report)
        return report

    def plot_confusion_matrix(self, y_true, y_pred, save_path="confusion_matrix.png"):
        """
        Generates a heatmap showing exactly which signs the model confuses[cite: 1].
        """
        cm = confusion_matrix(y_true, y_pred, labels=self.labels)
        
        # Convert to a DataFrame for easier plotting with Seaborn
        df_cm = pd.DataFrame(cm, index=self.labels, columns=self.labels)
        
        plt.figure(figsize=(10, 7))
        sns.heatmap(df_cm, annot=True, fmt='g', cmap='Blues')
        plt.title('SASL Gesture Confusion Matrix')
        plt.ylabel('Actual Sign')
        plt.xlabel('Predicted Sign')
        
        # Save the plot as an image file
        plt.savefig(save_path)
        print(f"\nConfusion matrix saved successfully to {save_path}")
        plt.close()

    def evaluate_model(self, y_true, y_pred):
        """
        Runs the full evaluation suite required for the final report[cite: 1].
        """
        self.generate_classification_report(y_true, y_pred)
        self.plot_confusion_matrix(y_true, y_pred)


# --- Test Execution ---
# Run this file directly to test the evaluation logic with dummy data
if __name__ == "__main__":
    # Dummy sign list from Group 3
    test_labels = ["HELLO", "THANK_YOU", "PLEASE", "YES", "NO"]
    
    # Generate 100 random synthetic "true" labels and "predicted" labels
    # This simulates the output of testing the model on the holdout validation set[cite: 1]
    np.random.seed(42)
    y_true_dummy = np.random.choice(test_labels, 100)
    
    # Intentionally introduce some errors to make the confusion matrix interesting
    y_pred_dummy = np.copy(y_true_dummy)
    error_indices = np.random.choice(100, 20, replace=False)
    y_pred_dummy[error_indices] = np.random.choice(test_labels, 20)

    evaluator = ModelEvaluator(labels=test_labels)
    evaluator.evaluate_model(y_true_dummy, y_pred_dummy)